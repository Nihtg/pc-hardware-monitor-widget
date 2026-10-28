using System.Collections.Generic;

namespace MiniHardwareWidget.Models;

public class FanDetail
{
    public string Name { get; set; } = string.Empty;
    public float Rpm { get; set; }
}

public class HardwareSnapshot
{
    // CPU
    public string CpuName { get; set; } = "CPU";
    public float CpuTemp { get; set; }
    public float CpuLoad { get; set; }
    public float CpuPower { get; set; }

    // GPU
    public string GpuName { get; set; } = "GPU";
    public float GpuTemp { get; set; }
    public float GpuHotSpotTemp { get; set; }
    public float GpuLoad { get; set; }
    public float GpuFanRpm { get; set; }
    public float GpuFanPercent { get; set; }
    public float GpuPower { get; set; }

    // Fans (Motherboard & Chassis)
    public float CpuFanRpm { get; set; }
    public List<FanDetail> Fans { get; set; } = new();

    // Power
    public float EstimatedBasePowerWatts { get; set; } = 35.0f;
    public float TotalWatts { get; set; }
    public double TotalWattHours { get; set; }

    // Network (Ethernet)
    public string NetworkName { get; set; } = "Ethernet";
    public string NetworkSpeedText { get; set; } = "—";
    public string NetworkStatus { get; set; } = "Disconnected";
    public bool IsNetworkUp { get; set; }
    public double DownloadBytesPerSec { get; set; }
    public double UploadBytesPerSec { get; set; }

    // VPN (Happ)
    public bool IsVpnActive { get; set; }
    public string VpnTitle { get; set; } = "VPN (Happ)";
    public string VpnCountryText { get; set; } = "—";
    public double VpnDownloadBytesPerSec { get; set; }
    public double VpnUploadBytesPerSec { get; set; }

    // Diagnostics
    public bool IsAdmin { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
}
