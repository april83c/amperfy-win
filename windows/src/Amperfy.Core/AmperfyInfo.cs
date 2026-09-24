using System.Reflection;

namespace Amperfy.Core;

public static class AmperfyInfo
{
    public const string Name = "Amperfy";

    /// Number of newest/recent elements fetched from the server (AmperKit.newestElementsFetchCount).
    public const int NewestElementsFetchCount = 50;

    public static string Version =>
        typeof(AmperfyInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
        ?? typeof(AmperfyInfo).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}
