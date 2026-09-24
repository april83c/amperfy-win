using System.Net.NetworkInformation;

namespace Amperfy.Core.Common;

public interface INetworkMonitor
{
    bool IsConnectedToNetwork { get; }
    /// Windows machines are treated as Wi-Fi/Ethernet unless a metered connection is reported.
    bool IsWifiOrEthernet { get; }
    bool IsCellular { get; }
    /// Raised on the main thread when connectivity changes (argument: isWifiOrEthernet).
    event Action<bool>? ConnectionTypeChanged;
}

/// Network monitor based on System.Net.NetworkInformation. The app can refine the metered
/// ("cellular") detection through <see cref="IsMeteredProvider"/>.
public sealed class NetworkMonitor : INetworkMonitor, IDisposable
{
    private volatile bool _isConnected;

    public Func<bool>? IsMeteredProvider { get; set; }

    public event Action<bool>? ConnectionTypeChanged;

    public NetworkMonitor()
    {
        _isConnected = SafeIsAvailable();
        NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
        NetworkChange.NetworkAddressChanged += OnAddressChanged;
    }

    private static bool SafeIsAvailable()
    {
        try { return NetworkInterface.GetIsNetworkAvailable(); } catch { return true; }
    }

    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) => Update(e.IsAvailable);

    private void OnAddressChanged(object? sender, EventArgs e) => Update(SafeIsAvailable());

    private void Update(bool isConnected)
    {
        var changed = _isConnected != isConnected;
        _isConnected = isConnected;
        if (!changed) return;
        AmperfyLog.Info("NetworkMonitor", isConnected ? "Connected" : "Disconnected: The network is not reachable");
        MainThread.Post(() => ConnectionTypeChanged?.Invoke(IsWifiOrEthernet));
    }

    public bool IsConnectedToNetwork => _isConnected;

    public bool IsCellular => _isConnected && (IsMeteredProvider?.Invoke() ?? false);

    public bool IsWifiOrEthernet => _isConnected && !IsCellular;

    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
        NetworkChange.NetworkAddressChanged -= OnAddressChanged;
    }
}

/// Always connected (tests).
public sealed class AlwaysOnlineNetworkMonitor : INetworkMonitor
{
    public bool IsConnectedToNetwork { get; set; } = true;
    public bool IsWifiOrEthernet => IsConnectedToNetwork;
    public bool IsCellular => false;
    public event Action<bool>? ConnectionTypeChanged;
    public void RaiseChanged() => ConnectionTypeChanged?.Invoke(IsWifiOrEthernet);
}
