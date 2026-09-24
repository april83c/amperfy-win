using Amperfy.App.Controls;
using Amperfy.App.Services.Audio;
using Amperfy.Core.Player;
using Amperfy.Core;
using Amperfy.Core.Common;
using Amperfy.Core.Model;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml;

namespace Amperfy.App.Services;

/// App-wide services (UI side of the composition root). Access via AppServices.Instance.
public sealed class AppServices
{
    public static AppServices Instance { get; private set; } = null!;

    public AmperKit Kit { get; }
    public LibraryStorage Library => Kit.Library;
    public AmperfySettings Settings => Kit.Settings;
    public EventLogger EventLogger => Kit.EventLogger;
    public EventNotificationHandler Notifications => Kit.NotificationHandler;
    public NavigationService Navigation { get; } = new();
    public DialogService Dialogs { get; } = new();
    public AlertService Alerts { get; }
    public MainWindow MainWindow { get; internal set; } = null!;
    public PlayerComponents PlayerComponents { get; }
    /// The app wide player (port of appDelegate.player).
    public IPlayerFacade Player => PlayerComponents.Player;
    public SleepTimer SleepTimer => PlayerComponents.SleepTimer;

    private readonly IDisposable _downloadRegistration;
    private bool _artworkRefreshPending;
    private IDisposable? _artworkRefreshTimer;

    private AppServices(AmperKit kit)
    {
        Kit = kit;
        Alerts = new AlertService(Dialogs);
        kit.EventLogger.AlertDisplayer = Alerts;
        PlayerComponents = kit.InitializePlayer(AudioBackend.CreateEngine, AudioBackend.CreateSystemMediaControls());
        // Downloaded artworks (and songs with embedded artworks): refresh visible images, debounced.
        _downloadRegistration = kit.NotificationHandler.Register(AmperfyNotification.DownloadFinishedSuccess, _ =>
        {
            if (_artworkRefreshPending) return;
            _artworkRefreshPending = true;
            _artworkRefreshTimer = MainThread.CreateTimer(TimeSpan.FromMilliseconds(700), () =>
            {
                _artworkRefreshPending = false;
                _artworkRefreshTimer?.Dispose();
                ArtworkImage.NotifyArtworkChanged();
            }, repeats: false);
        });
    }

    public static AppServices Initialize()
    {
        if (SynchronizationContext.Current is { } ctx) MainThread.Initialize(ctx);
        AmperfyLog.SetFileSink(AppPaths.LogFile);
        SecretProtection.Protector = new DpapiSecretProtector();
        CacheFileManager.Shared = new CacheFileManager(AppPaths.CacheDirectory);
        var storage = PersistentStorage.Open(AppPaths.DataDirectory);
        var kit = new AmperKit(storage, new NetworkMonitor());
        Instance = new AppServices(kit);
        ArtworkImage.SettingsProvider = account =>
        {
            var setting = kit.Settings.Accounts.GetSetting(account?.Info);
            return (setting.ArtworkDisplayPreference, setting.ThemePreference);
        };
        kit.UserStatistics.SessionStarted();
        SystemIntegration.RegisterProtocol();
        return Instance;
    }

    public Account? ActiveAccount => Kit.ActiveAccount;
    public MetaManager? ActiveMeta => Kit.ActiveMeta;

    public ThemePreference ActiveTheme => Settings.Accounts.ActiveSetting.ThemePreference;

    public DetailInfoType DetailInfo(DetailType type) => new(type, Settings, Library);

    public ServerApiType? ActiveApiType => ActiveAccount?.ApiType.AsServerApiType();

    public ElementTheme RequestedElementTheme => Settings.User.AppearanceMode switch
    {
        AppearanceMode.Light => ElementTheme.Light,
        AppearanceMode.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    public void Shutdown()
    {
        // queued main thread work (player timers, engine events) must not run on disposed storage
        MainThread.BeginShutdown();
        try { Kit.Dispose(); } catch (Exception ex) { CrashLog.Write($"Shutdown: {ex}"); }
        try { AudioBackend.Shutdown(); } catch (Exception ex) { CrashLog.Write($"Audio shutdown: {ex}"); }
    }
}
