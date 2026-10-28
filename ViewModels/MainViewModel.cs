using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using MiniHardwareWidget.Models;
using MiniHardwareWidget.Services;

namespace MiniHardwareWidget.ViewModels;

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly HardwareMonitorService _monitorService;

    private string _cpuName = "Intel Core i5-10400";
    private float _cpuTempRaw = 0;
    private string _cpuTempText = "— °C";
    private string _cpuLoadText = "— %";
    private string _cpuPowerText = "— W";

    private string _gpuName = "RTX 3060 Ti";
    private float _gpuTempRaw = 0;
    private string _gpuTempText = "— °C";
    private string _gpuHotSpotText = "— °C";
    private string _gpuLoadText = "— %";
    private string _gpuFanText = "— RPM";
    private string _gpuPowerText = "— W";

    private string _fanSummaryText = "Обороты: —";
    private bool _hasFans = false;

    private string _totalWattsText = "— W";
    private string _wattHoursText = "0.00 Wh";

    private string _networkName = "Ethernet";
    private string _networkSpeedText = "1.0 Gbps";
    private string _networkStatusText = "В сети";
    private double _downloadSpeedRaw = 0;
    private double _uploadSpeedRaw = 0;
    private bool _isNetworkUp = true;

    private bool _isVpnActive = false;
    private string _vpnTitle = "VPN (Happ)";
    private string _vpnCountryText = "—";
    private double _vpnDownloadSpeedRaw = 0;
    private double _vpnUploadSpeedRaw = 0;

    private bool _isAlwaysOnTop = true;
    private bool _isAdmin = false;
    private string _statusMessage = string.Empty;

    public MainViewModel()
    {
        App.LogDebug("MainViewModel constructor BEGIN");
        _isAdmin = HardwareMonitorService.CheckAdminStatus();

        _monitorService = new HardwareMonitorService(snapshot =>
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                ApplySnapshot(snapshot);
            });
        });

        App.LogDebug($"MainViewModel: IsAdmin={_isAdmin}");
        App.LogDebug("MainViewModel constructor END");
    }

    private void ApplySnapshot(HardwareSnapshot s)
    {
        CpuName = s.CpuName;
        CpuTempRaw = s.CpuTemp;
        if (s.CpuTemp > 0)
        {
            CpuTempText = $"{s.CpuTemp:0.#}°C";
        }
        else if (!s.IsAdmin)
        {
            CpuTempText = "Требует Admin";
        }
        else
        {
            CpuTempText = "— °C";
        }

        CpuLoadText = $"{s.CpuLoad:0}%";
        CpuPowerText = s.CpuPower > 0 ? $"{s.CpuPower:0.#} W" : "— W";

        GpuName = s.GpuName;
        GpuTempRaw = s.GpuTemp;
        GpuTempText = s.GpuTemp > 0 ? $"{s.GpuTemp:0.#}°C" : "—";
        GpuHotSpotText = s.GpuHotSpotTemp > 0 ? $"{s.GpuHotSpotTemp:0.#}°C" : string.Empty;
        GpuLoadText = $"{s.GpuLoad:0}%";

        if (s.GpuFanRpm > 0)
        {
            GpuFanText = s.GpuFanPercent > 0 ? $"{s.GpuFanRpm:0} RPM ({s.GpuFanPercent:0}%)" : $"{s.GpuFanRpm:0} RPM";
        }
        else
        {
            GpuFanText = "0 RPM (Passiv)";
        }

        GpuPowerText = s.GpuPower > 0 ? $"{s.GpuPower:0.#} W" : "— W";

        // Fans
        var allFans = s.Fans.Where(f => f.Rpm > 0).ToList();
        if (allFans.Count > 0)
        {
            HasFans = true;
            FanSummaryText = string.Join(" | ", allFans.Take(3).Select(f => $"{f.Name}: {f.Rpm:0} RPM"));
        }
        else if (s.CpuFanRpm > 0)
        {
            HasFans = true;
            FanSummaryText = $"CPU Fan: {s.CpuFanRpm:0} RPM";
        }
        else
        {
            HasFans = false;
            FanSummaryText = _isAdmin ? "Вентиляторы: 0 RPM или авто" : "Запустите от админа для RPM";
        }

        // Power
        TotalWattsText = $"{s.TotalWatts:0.#} W";

        if (s.TotalWattHours >= 1000)
        {
            WattHoursText = $"{s.TotalWattHours / 1000.0:0.00} kWh";
        }
        else
        {
            WattHoursText = $"{s.TotalWattHours:0.0} Wh";
        }

        // Network
        NetworkName = s.NetworkName;
        NetworkSpeedText = s.NetworkSpeedText;
        NetworkStatusText = s.NetworkStatus;
        IsNetworkUp = s.IsNetworkUp;
        DownloadSpeedRaw = s.DownloadBytesPerSec;
        UploadSpeedRaw = s.UploadBytesPerSec;

        // VPN (Happ)
        IsVpnActive = s.IsVpnActive;
        VpnTitle = s.VpnTitle;
        VpnCountryText = s.VpnCountryText;
        VpnDownloadSpeedRaw = s.VpnDownloadBytesPerSec;
        VpnUploadSpeedRaw = s.VpnUploadBytesPerSec;

        IsAdmin = s.IsAdmin;
        StatusMessage = s.StatusMessage;
    }

    public void ToggleAlwaysOnTop()
    {
        IsAlwaysOnTop = !IsAlwaysOnTop;
    }

    public void ResetWattHours()
    {
        _monitorService.ResetWattHours();
        WattHoursText = "0.0 Wh";
    }

    public static void RestartAsAdmin()
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                FileName = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName,
                Verb = "runas"
            };

            Process.Start(processInfo);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось перезапустить с правами администратора: {ex.Message}", "Ошибка UAC", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void Dispose()
    {
        _monitorService.Dispose();
    }

    // Properties
    public string CpuName { get => _cpuName; set => SetField(ref _cpuName, value); }
    public float CpuTempRaw { get => _cpuTempRaw; set => SetField(ref _cpuTempRaw, value); }
    public string CpuTempText { get => _cpuTempText; set => SetField(ref _cpuTempText, value); }
    public string CpuLoadText { get => _cpuLoadText; set => SetField(ref _cpuLoadText, value); }
    public string CpuPowerText { get => _cpuPowerText; set => SetField(ref _cpuPowerText, value); }

    public string GpuName { get => _gpuName; set => SetField(ref _gpuName, value); }
    public float GpuTempRaw { get => _gpuTempRaw; set => SetField(ref _gpuTempRaw, value); }
    public string GpuTempText { get => _gpuTempText; set => SetField(ref _gpuTempText, value); }
    public string GpuHotSpotText { get => _gpuHotSpotText; set => SetField(ref _gpuHotSpotText, value); }
    public string GpuLoadText { get => _gpuLoadText; set => SetField(ref _gpuLoadText, value); }
    public string GpuFanText { get => _gpuFanText; set => SetField(ref _gpuFanText, value); }
    public string GpuPowerText { get => _gpuPowerText; set => SetField(ref _gpuPowerText, value); }

    public string FanSummaryText { get => _fanSummaryText; set => SetField(ref _fanSummaryText, value); }
    public bool HasFans { get => _hasFans; set => SetField(ref _hasFans, value); }

    public string TotalWattsText { get => _totalWattsText; set => SetField(ref _totalWattsText, value); }
    public string WattHoursText { get => _wattHoursText; set => SetField(ref _wattHoursText, value); }

    public string NetworkName { get => _networkName; set => SetField(ref _networkName, value); }
    public string NetworkSpeedText { get => _networkSpeedText; set => SetField(ref _networkSpeedText, value); }
    public string NetworkStatusText { get => _networkStatusText; set => SetField(ref _networkStatusText, value); }
    public double DownloadSpeedRaw { get => _downloadSpeedRaw; set => SetField(ref _downloadSpeedRaw, value); }
    public double UploadSpeedRaw { get => _uploadSpeedRaw; set => SetField(ref _uploadSpeedRaw, value); }
    public bool IsNetworkUp { get => _isNetworkUp; set => SetField(ref _isNetworkUp, value); }

    public bool IsVpnActive { get => _isVpnActive; set => SetField(ref _isVpnActive, value); }
    public string VpnTitle { get => _vpnTitle; set => SetField(ref _vpnTitle, value); }
    public string VpnCountryText { get => _vpnCountryText; set => SetField(ref _vpnCountryText, value); }
    public double VpnDownloadSpeedRaw { get => _vpnDownloadSpeedRaw; set => SetField(ref _vpnDownloadSpeedRaw, value); }
    public double VpnUploadSpeedRaw { get => _vpnUploadSpeedRaw; set => SetField(ref _vpnUploadSpeedRaw, value); }

    public bool IsAlwaysOnTop { get => _isAlwaysOnTop; set => SetField(ref _isAlwaysOnTop, value); }
    public bool IsAdmin { get => _isAdmin; set => SetField(ref _isAdmin, value); }
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
