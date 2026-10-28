using System;
using System.Linq;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace MiniHardwareWidget.Services;

public class NetworkMetrics
{
    public string AdapterName { get; set; } = "Ethernet";
    public string LinkSpeedText { get; set; } = "—";
    public string Status { get; set; } = "Отключено";
    public bool IsUp { get; set; }
    public double DownloadBytesPerSec { get; set; }
    public double UploadBytesPerSec { get; set; }

    // VPN (Happ)
    public bool IsVpnActive { get; set; }
    public string VpnTitle { get; set; } = "VPN (Happ)";
    public string VpnCountryText { get; set; } = "—";
    public double VpnDownloadBytesPerSec { get; set; }
    public double VpnUploadBytesPerSec { get; set; }
}

public class NetworkMonitorService
{
    private long _lastBytesReceived = -1;
    private long _lastBytesSent = -1;
    private long _lastVpnBytesReceived = -1;
    private long _lastVpnBytesSent = -1;
    private DateTime _lastPollTime = DateTime.MinValue;
    private string? _cachedInterfaceId;

    public NetworkMetrics GetMetrics()
    {
        var result = new NetworkMetrics();
        DateTime now = DateTime.UtcNow;

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();

            // 1. Primary Physical Network (Ethernet)
            NetworkInterface? selected = null;

            if (!string.IsNullOrEmpty(_cachedInterfaceId))
            {
                selected = interfaces.FirstOrDefault(i => i.Id == _cachedInterfaceId);
            }

            if (selected == null || selected.OperationalStatus != OperationalStatus.Up)
            {
                selected = interfaces.FirstOrDefault(i =>
                    i.NetworkInterfaceType == NetworkInterfaceType.Ethernet &&
                    !IsVirtualOrVpn(i) &&
                    i.OperationalStatus == OperationalStatus.Up);

                // Fallback to physical Wi-Fi if Ethernet is down
                selected ??= interfaces.FirstOrDefault(i =>
                    (i.NetworkInterfaceType == NetworkInterfaceType.Ethernet || i.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) &&
                    !IsVirtualOrVpn(i) &&
                    i.OperationalStatus == OperationalStatus.Up);

                selected ??= interfaces.FirstOrDefault(i =>
                    i.NetworkInterfaceType == NetworkInterfaceType.Ethernet &&
                    !IsVirtualOrVpn(i));

                if (selected != null)
                {
                    _cachedInterfaceId = selected.Id;
                }
            }

            if (selected != null)
            {
                result.AdapterName = selected.Name;
                result.IsUp = selected.OperationalStatus == OperationalStatus.Up;
                result.Status = result.IsUp ? "В сети (Up)" : "Отключен";

                if (result.IsUp && selected.Speed > 0)
                {
                    result.LinkSpeedText = FormatLinkSpeed(selected.Speed);
                }
                else
                {
                    result.LinkSpeedText = "Нет связи";
                }

                try
                {
                    var stats = selected.GetIPv4Statistics();
                    long currentRecv = stats.BytesReceived;
                    long currentSent = stats.BytesSent;

                    if (_lastBytesReceived >= 0 && _lastPollTime > DateTime.MinValue)
                    {
                        double elapsed = (now - _lastPollTime).TotalSeconds;
                        if (elapsed > 0.1)
                        {
                            double recvDiff = currentRecv - _lastBytesReceived;
                            double sentDiff = currentSent - _lastBytesSent;

                            result.DownloadBytesPerSec = recvDiff > 0 ? recvDiff / elapsed : 0;
                            result.UploadBytesPerSec = sentDiff > 0 ? sentDiff / elapsed : 0;
                        }
                    }

                    _lastBytesReceived = currentRecv;
                    _lastBytesSent = currentSent;
                }
                catch { }
            }

            // 2. Happ VPN Tunnel Monitoring
            var vpnInterface = interfaces.FirstOrDefault(i =>
                (i.Name.Equals("happ-tun", StringComparison.OrdinalIgnoreCase) ||
                 i.Description.ToLowerInvariant().Contains("sing-tun")) &&
                i.OperationalStatus == OperationalStatus.Up);

            if (vpnInterface != null)
            {
                result.IsVpnActive = true;
                string? rawServerName = GetHappServerNameFromRegistry();
                result.VpnCountryText = FormatVpnCountry(rawServerName);

                try
                {
                    var vpnStats = vpnInterface.GetIPv4Statistics();
                    long vpnRecv = vpnStats.BytesReceived;
                    long vpnSent = vpnStats.BytesSent;

                    if (_lastVpnBytesReceived >= 0 && _lastPollTime > DateTime.MinValue)
                    {
                        double elapsed = (now - _lastPollTime).TotalSeconds;
                        if (elapsed > 0.1)
                        {
                            double vpnRecvDiff = vpnRecv - _lastVpnBytesReceived;
                            double vpnSentDiff = vpnSent - _lastVpnBytesSent;

                            result.VpnDownloadBytesPerSec = vpnRecvDiff > 0 ? vpnRecvDiff / elapsed : 0;
                            result.VpnUploadBytesPerSec = vpnSentDiff > 0 ? vpnSentDiff / elapsed : 0;
                        }
                    }

                    _lastVpnBytesReceived = vpnRecv;
                    _lastVpnBytesSent = vpnSent;
                }
                catch { }
            }
            else
            {
                result.IsVpnActive = false;
                _lastVpnBytesReceived = -1;
                _lastVpnBytesSent = -1;
            }

            _lastPollTime = now;
        }
        catch
        {
            result.Status = "Ошибка сети";
        }

        return result;
    }

    private static string? GetHappServerNameFromRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Happ\OrganizationDefaults\Preferences");
            var val = key?.GetValue("lastServerName") as string;
            if (!string.IsNullOrEmpty(val)) return val;
        }
        catch { }

        // Fallback for elevated administrator session: check user SID hives
        try
        {
            foreach (var subKeyName in Registry.Users.GetSubKeyNames())
            {
                if (subKeyName.StartsWith("S-1-5-21-") && !subKeyName.EndsWith("_Classes"))
                {
                    using var uKey = Registry.Users.OpenSubKey($@"{subKeyName}\Software\Happ\OrganizationDefaults\Preferences");
                    var val = uKey?.GetValue("lastServerName") as string;
                    if (!string.IsNullOrEmpty(val)) return val;
                }
            }
        }
        catch { }

        return null;
    }

    public static string FormatVpnCountry(string? serverName)
    {
        if (string.IsNullOrWhiteSpace(serverName)) return "В сети";

        string trimmed = serverName.Trim();
        string lower = trimmed.ToLowerInvariant();

        string flag = "";
        string country = "";

        if (lower.Contains("швеци") || lower.Contains("sweden") || lower.Contains("se-") || lower.Contains("-se"))
        { flag = "🇸🇪"; country = "Швеция"; }
        else if (lower.Contains("герман") || lower.Contains("germany") || lower.Contains("frankfurt") || lower.Contains("de-") || lower.Contains("-de"))
        { flag = "🇩🇪"; country = "Германия"; }
        else if (lower.Contains("нидерланд") || lower.Contains("netherlands") || lower.Contains("голланди") || lower.Contains("amsterdam") || lower.Contains("nl-") || lower.Contains("-nl"))
        { flag = "🇳🇱"; country = "Нидерланды"; }
        else if (lower.Contains("сша") || lower.Contains("usa") || lower.Contains("united states") || lower.Contains("us-") || lower.Contains("-us"))
        { flag = "🇺🇸"; country = "США"; }
        else if (lower.Contains("финлянд") || lower.Contains("finland") || lower.Contains("helsinki") || lower.Contains("fi-") || lower.Contains("-fi"))
        { flag = "🇫🇮"; country = "Финляндия"; }
        else if (lower.Contains("польш") || lower.Contains("poland") || lower.Contains("warsaw") || lower.Contains("pl-") || lower.Contains("-pl"))
        { flag = "🇵🇱"; country = "Польша"; }
        else if (lower.Contains("франци") || lower.Contains("france") || lower.Contains("paris") || lower.Contains("fr-") || lower.Contains("-fr"))
        { flag = "🇫🇷"; country = "Франция"; }
        else if (lower.Contains("великобритан") || lower.Contains("england") || lower.Contains("london") || lower.Contains("uk-") || lower.Contains("gb-"))
        { flag = "🇬🇧"; country = "Великобритания"; }
        else if (lower.Contains("турци") || lower.Contains("turkey") || lower.Contains("istanbul") || lower.Contains("tr-") || lower.Contains("-tr"))
        { flag = "🇹🇷"; country = "Турция"; }
        else if (lower.Contains("казахстан") || lower.Contains("kazakhstan") || lower.Contains("almaty") || lower.Contains("astana") || lower.Contains("kz-"))
        { flag = "🇰🇿"; country = "Казахстан"; }
        else if (lower.Contains("эстони") || lower.Contains("estonia") || lower.Contains("tallinn") || lower.Contains("ee-"))
        { flag = "🇪🇪"; country = "Эстония"; }
        else if (lower.Contains("латви") || lower.Contains("latvia") || lower.Contains("riga") || lower.Contains("lv-"))
        { flag = "🇱🇻"; country = "Латвия"; }
        else if (lower.Contains("литв") || lower.Contains("lithuania") || lower.Contains("vilnius") || lower.Contains("lt-"))
        { flag = "🇱🇹"; country = "Литва"; }
        else if (lower.Contains("швейцар") || lower.Contains("switzerland") || lower.Contains("zurich") || lower.Contains("ch-"))
        { flag = "🇨🇭"; country = "Швейцария"; }
        else if (lower.Contains("австри") || lower.Contains("austria") || lower.Contains("vienna") || lower.Contains("at-"))
        { flag = "🇦🇹"; country = "Австрия"; }
        else if (lower.Contains("испани") || lower.Contains("spain") || lower.Contains("madrid") || lower.Contains("es-"))
        { flag = "🇪🇸"; country = "Испания"; }
        else if (lower.Contains("итали") || lower.Contains("italy") || lower.Contains("rome") || lower.Contains("milan") || lower.Contains("it-"))
        { flag = "🇮🇹"; country = "Италия"; }
        else if (lower.Contains("чехи") || lower.Contains("czech") || lower.Contains("prague") || lower.Contains("cz-"))
        { flag = "🇨🇿"; country = "Чехия"; }
        else if (lower.Contains("япони") || lower.Contains("japan") || lower.Contains("tokyo") || lower.Contains("jp-"))
        { flag = "🇯🇵"; country = "Япония"; }
        else if (lower.Contains("сингапур") || lower.Contains("singapore") || lower.Contains("sg-"))
        { flag = "🇸🇬"; country = "Сингапур"; }
        else if (lower.Contains("норвеги") || lower.Contains("norway") || lower.Contains("oslo") || lower.Contains("no-"))
        { flag = "🇳🇴"; country = "Норвегия"; }
        else if (lower.Contains("росси") || lower.Contains("russia") || lower.Contains("moscow") || lower.Contains("ru-"))
        { flag = "🇷🇺"; country = "Россия"; }

        // Extract server index if present, e.g. "#1" or "01"
        string serverIndex = "";
        var hashIdx = trimmed.IndexOf('#');
        if (hashIdx >= 0)
        {
            var endIdx = trimmed.IndexOfAny(new[] { ' ', '-', '_' }, hashIdx);
            serverIndex = endIdx > hashIdx ? trimmed.Substring(hashIdx, endIdx - hashIdx) : trimmed.Substring(hashIdx);
        }

        if (!string.IsNullOrEmpty(country))
        {
            return string.IsNullOrEmpty(serverIndex) ? $"{flag} {country}" : $"{flag} {country} {serverIndex}";
        }

        var clean = trimmed.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        return $"🔒 {clean}";
    }

    private static bool IsVirtualOrVpn(NetworkInterface ni)
    {
        string desc = (ni.Description + " " + ni.Name).ToLowerInvariant();
        return desc.Contains("virtual") ||
               desc.Contains("tap-") ||
               desc.Contains("vpn") ||
               desc.Contains("tunnel") ||
               desc.Contains("sing-tun") ||
               desc.Contains("sing-box") ||
               desc.Contains("happ-tun") ||
               desc.Contains("wireguard") ||
               desc.Contains("wsl") ||
               desc.Contains("hyper-v") ||
               desc.Contains("vmware") ||
               desc.Contains("bluetooth") ||
               desc.Contains("loopback");
    }

    private static string FormatLinkSpeed(long speedBps)
    {
        if (speedBps >= 10_000_000_000) return $"{speedBps / 1_000_000_000.0:0.0} Gbps";
        if (speedBps >= 1_000_000_000) return $"{speedBps / 1_000_000_000.0:0.0} Gbps";
        if (speedBps >= 1_000_000) return $"{speedBps / 1_000_000} Mbps";
        if (speedBps >= 1_000) return $"{speedBps / 1_000} Kbps";
        return $"{speedBps} bps";
    }
}
