using Windows.Networking.Connectivity;

namespace Amperfy.App.Services;

/// Detects metered internet connections (mobile hotspot, connections set as metered in Windows).
/// The core treats a metered connection like "cellular" (streaming settings "Metered network").
/// Results are cached briefly because the check runs for every stream request.
public static class MeteredConnectionDetector
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);
    private static readonly object Lock = new();
    private static DateTime _checkedAt = DateTime.MinValue;
    private static bool _isMetered;

    public static bool IsMetered()
    {
        lock (Lock)
        {
            if (DateTime.UtcNow - _checkedAt < CacheDuration) return _isMetered;
            _isMetered = Check();
            _checkedAt = DateTime.UtcNow;
            return _isMetered;
        }
    }

    private static bool Check()
    {
        try
        {
            var cost = NetworkInformation.GetInternetConnectionProfile()?.GetConnectionCost();
            if (cost is null) return false;
            return cost.NetworkCostType is NetworkCostType.Fixed or NetworkCostType.Variable || cost.Roaming || cost.OverDataLimit;
        }
        catch
        {
            return false;
        }
    }
}
