using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using LibreHardwareMonitor.Hardware;
using MiniHardwareWidget.Models;

namespace MiniHardwareWidget.Services;

public class HardwareMonitorService : IDisposable
{
    private Computer? _computer;
    private Thread? _workerThread;
    private readonly CancellationTokenSource _cts = new();
    private readonly Action<HardwareSnapshot> _onSnapshot;
    private readonly NetworkMonitorService _networkMonitor;
    private DateTime _lastPollTime = DateTime.MinValue;
    private double _accumulatedWattHours = 0.0;
    private readonly bool _isAdmin;

    public HardwareMonitorService(Action<HardwareSnapshot> onSnapshot)
    {
        _onSnapshot = onSnapshot;
        _networkMonitor = new NetworkMonitorService();
        _isAdmin = CheckAdminStatus();

        _workerThread = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "HardwareMonitorWorker"
        };
        _workerThread.Start();
    }

    public static bool CheckAdminStatus()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public void ResetWattHours()
    {
        _accumulatedWattHours = 0.0;
    }

    private void WorkerLoop()
    {
        App.LogDebug($"WorkerLoop started on dedicated thread ID: {Environment.CurrentManagedThreadId}");

        if (_isAdmin)
        {
            DriverServiceHelper.EnsureDriverServiceRunning();
        }

        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsMemoryEnabled = false,
            IsStorageEnabled = false,
            IsNetworkEnabled = false
        };

        try
        {
            App.LogDebug("HardwareMonitorService: Computer.Open() calling...");
            _computer.Open();
            App.LogDebug("HardwareMonitorService: Computer.Open() succeeded");
        }
        catch (Exception ex)
        {
            App.LogDebug($"HardwareMonitorService: Computer.Open() failed: {ex}");
        }

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var snapshot = PollInternal();
                _onSnapshot?.Invoke(snapshot);
            }
            catch (Exception ex)
            {
                App.LogDebug($"Poll exception in WorkerLoop: {ex}");
            }

            try
            {
                Thread.Sleep(1000);
            }
            catch
            {
                break;
            }
        }

        try
        {
            _computer?.Close();
            App.LogDebug("HardwareMonitorService: Computer closed");
        }
        catch { }
    }

    private HardwareSnapshot PollInternal()
    {
        App.LogDebug("PollInternal START");
        DateTime now = DateTime.UtcNow;
        var snapshot = new HardwareSnapshot
        {
            IsAdmin = _isAdmin
        };

        if (_computer != null)
        {
            try
            {
                App.LogDebug("PollInternal: updating hardware list...");
                foreach (var hardware in _computer.Hardware)
                {
                    try
                    {
                        App.LogDebug($"PollInternal: updating {hardware.Name}");
                        hardware.Update();
                        foreach (var sub in hardware.SubHardware)
                        {
                            try { sub.Update(); } catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        App.LogDebug($"PollInternal: error updating {hardware.Name}: {ex.Message}");
                    }
                }
                App.LogDebug("PollInternal: hardware update finished");

                // 1. CPU Sensors
                App.LogDebug("PollInternal: CPU sensors...");
                var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
                if (cpu != null)
                {
                    snapshot.CpuName = CleanHardwareName(cpu.Name);

                    // CPU Load
                    var cpuLoad = cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Total"));
                    if (cpuLoad != null && cpuLoad.Value.HasValue)
                    {
                        snapshot.CpuLoad = cpuLoad.Value.Value;
                    }

                    // CPU Temperature
                    var cpuPackageTemp = cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Value.HasValue && s.Value > 0 && !s.Name.Contains("Distance") && (s.Name.Contains("Package") || s.Name.Contains("Core Average") || s.Name.Contains("Core Max")));
                    if (cpuPackageTemp != null && cpuPackageTemp.Value.HasValue)
                    {
                        snapshot.CpuTemp = cpuPackageTemp.Value.Value;
                    }
                    else
                    {
                        var coreTemps = cpu.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue && s.Value > 0 && !s.Name.Contains("Distance")).ToList();
                        if (coreTemps.Count > 0)
                        {
                            snapshot.CpuTemp = coreTemps.Max(s => s.Value!.Value);
                        }
                        else
                        {
                            // Fallback: check Motherboard / SuperIO thermal sensors for CPU socket temp
                            var mb = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Motherboard);
                            if (mb != null)
                            {
                                var mbCpuTemp = FindSensorRecursive(mb, s => s.SensorType == SensorType.Temperature && s.Value.HasValue && s.Value > 0 && !s.Name.Contains("Distance") && (s.Name.Contains("CPU") || s.Name.Contains("Core")));
                                if (mbCpuTemp != null && mbCpuTemp.Value.HasValue)
                                {
                                    snapshot.CpuTemp = mbCpuTemp.Value.Value;
                                }
                            }
                        }
                    }

                    // Global fallback for CPU temperature across all motherboard / controller devices
                    if (snapshot.CpuTemp <= 0)
                    {
                        foreach (var hw in _computer.Hardware)
                        {
                            var anyCpuTemp = FindSensorRecursive(hw, s => s.SensorType == SensorType.Temperature 
                                && s.Value.HasValue && s.Value > 0 
                                && !s.Name.Contains("Distance") 
                                && (s.Name.Contains("CPU") || hw.Name.Contains("CPU") || hw.HardwareType == HardwareType.Cpu));
                            if (anyCpuTemp?.Value != null && anyCpuTemp.Value > 0)
                            {
                                snapshot.CpuTemp = anyCpuTemp.Value.Value;
                                break;
                            }
                        }
                    }

                    // CPU Power
                    var cpuPower = cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Value.HasValue && s.Value > 0 && (s.Name.Contains("Package") || s.Name.Contains("Total")));
                    if (cpuPower != null && cpuPower.Value.HasValue)
                    {
                        snapshot.CpuPower = cpuPower.Value.Value;
                    }
                    else
                    {
                        var anyCpuPower = cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Value.HasValue && s.Value > 0);
                        if (anyCpuPower != null && anyCpuPower.Value.HasValue)
                        {
                            snapshot.CpuPower = anyCpuPower.Value.Value;
                        }
                        else if (snapshot.CpuLoad > 0)
                        {
                            // Accurate estimation based on CPU load for i5-10400 (TDP 65W, idle 12W)
                            snapshot.CpuPower = 12.0f + (snapshot.CpuLoad / 100.0f) * 53.0f;
                        }
                    }
                }

                // 2. GPU Sensors (Prioritize discrete Nvidia/AMD over Intel iGPU)
                App.LogDebug("PollInternal: GPU sensors...");
                var gpus = _computer.Hardware.Where(h =>
                    h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel).ToList();

                var discreteGpu = gpus.FirstOrDefault(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd) ?? gpus.FirstOrDefault();

                if (discreteGpu != null)
                {
                    snapshot.GpuName = CleanHardwareName(discreteGpu.Name);

                    // GPU Temp
                    var gpuCoreTemp = discreteGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && (s.Name.Contains("Core") || s.Name == "GPU"));
                    if (gpuCoreTemp != null && gpuCoreTemp.Value.HasValue)
                    {
                        snapshot.GpuTemp = gpuCoreTemp.Value.Value;
                    }

                    var gpuHotSpot = discreteGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Hot Spot"));
                    if (gpuHotSpot != null && gpuHotSpot.Value.HasValue)
                    {
                        snapshot.GpuHotSpotTemp = gpuHotSpot.Value.Value;
                    }

                    // GPU Load
                    var gpuLoad = discreteGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && (s.Name.Contains("Core") || s.Name == "GPU"));
                    if (gpuLoad != null && gpuLoad.Value.HasValue)
                    {
                        snapshot.GpuLoad = gpuLoad.Value.Value;
                    }

                    // GPU Fans (RPM and %)
                    var gpuFanRpm = discreteGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Fan && s.Value.HasValue && s.Value > 0);
                    if (gpuFanRpm != null && gpuFanRpm.Value.HasValue)
                    {
                        snapshot.GpuFanRpm = gpuFanRpm.Value.Value;
                    }

                    var gpuFanCtrl = discreteGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Control && s.Value.HasValue);
                    if (gpuFanCtrl != null && gpuFanCtrl.Value.HasValue)
                    {
                        snapshot.GpuFanPercent = gpuFanCtrl.Value.Value;
                    }

                    // GPU Power
                    var gpuPower = discreteGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && (s.Name.Contains("GPU Package") || s.Name.Contains("Total") || s.Name.Contains("Board")));
                    gpuPower ??= discreteGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power);
                    if (gpuPower != null && gpuPower.Value.HasValue)
                    {
                        snapshot.GpuPower = gpuPower.Value.Value;
                    }
                }

                // 3. Motherboard & Fans
                App.LogDebug("PollInternal: Motherboard fans...");
                var fanList = new List<FanDetail>();
                float cpuFan = 0;

                foreach (var hardware in _computer.Hardware)
                {
                    GatherFans(hardware, fanList, ref cpuFan);
                }

                snapshot.CpuFanRpm = cpuFan;
                snapshot.Fans = fanList;

                // 4. Power & Energy calculations
                App.LogDebug("PollInternal: Power calculation...");
                float currentWatts = 0;
                if (snapshot.CpuPower > 0) currentWatts += snapshot.CpuPower;
                else currentWatts += 20.0f;

                if (snapshot.GpuPower > 0) currentWatts += snapshot.GpuPower;
                else currentWatts += 15.0f;

                currentWatts += snapshot.EstimatedBasePowerWatts;
                snapshot.TotalWatts = currentWatts;

                if (_lastPollTime > DateTime.MinValue)
                {
                    double dt = (now - _lastPollTime).TotalSeconds;
                    if (dt > 0.1 && dt < 10.0)
                    {
                        _accumulatedWattHours += currentWatts * (dt / 3600.0);
                    }
                }
                snapshot.TotalWattHours = _accumulatedWattHours;
                _lastPollTime = now;
            }
            catch (Exception ex)
            {
                App.LogDebug($"PollInternal sensor error: {ex}");
                snapshot.StatusMessage = $"Ошибка датчиков: {ex.Message}";
            }
        }

        // 5. Network Metrics
        try
        {
            App.LogDebug("PollInternal: Network metrics...");
            var net = _networkMonitor.GetMetrics();
            snapshot.NetworkName = net.AdapterName;
            snapshot.NetworkSpeedText = net.LinkSpeedText;
            snapshot.NetworkStatus = net.Status;
            snapshot.IsNetworkUp = net.IsUp;
            snapshot.DownloadBytesPerSec = net.DownloadBytesPerSec;
            snapshot.UploadBytesPerSec = net.UploadBytesPerSec;

            // VPN (Happ)
            snapshot.IsVpnActive = net.IsVpnActive;
            snapshot.VpnTitle = net.VpnTitle;
            snapshot.VpnCountryText = net.VpnCountryText;
            snapshot.VpnDownloadBytesPerSec = net.VpnDownloadBytesPerSec;
            snapshot.VpnUploadBytesPerSec = net.VpnUploadBytesPerSec;
            App.LogDebug("PollInternal: Network metrics finished");
        }
        catch (Exception ex)
        {
            App.LogDebug($"PollInternal network error: {ex}");
            snapshot.NetworkStatus = $"Ошибка сети: {ex.Message}";
        }

        App.LogDebug("PollInternal END");
        return snapshot;
    }

    private static ISensor? FindSensorRecursive(IHardware hardware, Func<ISensor, bool> predicate)
    {
        foreach (var s in hardware.Sensors)
        {
            if (predicate(s)) return s;
        }

        foreach (var sub in hardware.SubHardware)
        {
            var found = FindSensorRecursive(sub, predicate);
            if (found != null) return found;
        }

        return null;
    }

    private static void GatherFans(IHardware hardware, List<FanDetail> fanList, ref float cpuFan)
    {
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.SensorType == SensorType.Fan && sensor.Value.HasValue && sensor.Value > 0)
            {
                string name = sensor.Name;
                float rpm = sensor.Value.Value;

                if (name.Contains("CPU", StringComparison.OrdinalIgnoreCase) && cpuFan == 0)
                {
                    cpuFan = rpm;
                }

                if (!fanList.Any(f => f.Name == name))
                {
                    fanList.Add(new FanDetail { Name = name, Rpm = rpm });
                }
            }
        }

        foreach (var sub in hardware.SubHardware)
        {
            GatherFans(sub, fanList, ref cpuFan);
        }
    }

    private static string CleanHardwareName(string name)
    {
        return name
            .Replace("Intel(R) Core(TM) ", "")
            .Replace("NVIDIA ", "")
            .Replace("AMD ", "")
            .Replace("CPU @ ", "@ ")
            .Trim();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
