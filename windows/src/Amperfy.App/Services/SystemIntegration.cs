using Amperfy.Core.Common;
using Amperfy.Core.Intents;
using Microsoft.Windows.AppLifecycle;

namespace Amperfy.App.Services;

/// Windows integration of the amperfy:// automation URLs (x-callback-url, port of IntentManager).
/// Toast notifications: ToastNotificationService; metered networks: MeteredConnectionDetector.
public static class SystemIntegration
{
    /// Registers the amperfy:// protocol (x-callback-url automation) for the current user.
    public static void RegisterProtocol()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is null) return;
            ActivationRegistrationManager.RegisterForProtocolActivation(UrlCommandHandler.Scheme, $"{exe},0", "Amperfy", exe);
        }
        catch (Exception ex)
        {
            AmperfyLog.Error("SystemIntegration", $"Protocol registration failed: {ex.Message}");
        }
    }

    /// Finds an amperfy:// URL in command line arguments.
    public static Uri? FindCommandUrl(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return null;
        foreach (var raw in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = raw.Trim('"');
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && UrlCommandHandler.IsCommandUrl(uri)) return uri;
        }
        return null;
    }

    private static UrlCommandHandler? _urlCommandHandler;

    /// Executes an amperfy://x-callback-url/... command and opens the requested callback URL.
    public static async void HandleCommandUrl(Uri url)
    {
        try
        {
            var services = AppServices.Instance;
            if (!UrlCommandHandler.IsCommandUrl(url) || !services.Kit.IsLoggedIn) return;
            _urlCommandHandler ??= new UrlCommandHandler(services.Library, services.Settings, services.Player, services.Kit.NetworkMonitor,
                () => services.ActiveAccount, info => services.Kit.GetMeta(info).LibrarySyncer);
            var result = await _urlCommandHandler.HandleAsync(url);
            if (!result.Success && result.CallbackUrl is null)
                services.EventLogger.Info("URL Command", result.ErrorMessage ?? "Command failed", displayPopup: true);
            if (result.CallbackUrl is { } callback) await Windows.System.Launcher.LaunchUriAsync(callback);
        }
        catch (Exception ex)
        {
            AmperfyLog.Error("SystemIntegration", $"URL command failed: {ex}");
        }
    }

    /// Documentation of the supported automation URLs (shown in the settings).
    public static IReadOnlyList<UrlCommandDocu> UrlCommandDocumentation
    {
        get
        {
            var services = AppServices.Instance;
            _urlCommandHandler ??= new UrlCommandHandler(services.Library, services.Settings, services.Player, services.Kit.NetworkMonitor,
                () => services.ActiveAccount, info => services.Kit.GetMeta(info).LibrarySyncer);
            return _urlCommandHandler.Documentation;
        }
    }
}
