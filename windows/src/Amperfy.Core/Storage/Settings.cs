using System.Text.Json;
using System.Text.Json.Serialization;

namespace Amperfy.Core.Storage;

public sealed class AppSettings : ObservableSettings
{
    private bool _isLibrarySyncInfoReadByUser;
    public bool IsLibrarySyncInfoReadByUser { get => _isLibrarySyncInfoReadByUser; set => SetField(ref _isLibrarySyncInfoReadByUser, value); }

    private bool _isLibrarySynced;
    public bool IsLibrarySynced { get => _isLibrarySynced; set => SetField(ref _isLibrarySynced, value); }

    private LibrarySyncVersion _librarySyncVersion = SettingEnumerationExtensions.NewestLibrarySyncVersion;
    public LibrarySyncVersion LibrarySyncVersion { get => _librarySyncVersion; set => SetField(ref _librarySyncVersion, value); }

    // Windows specific: main window placement
    private int[]? _mainWindowBounds;
    public int[]? MainWindowBounds { get => _mainWindowBounds; set => SetField(ref _mainWindowBounds, value); }

    private bool _isMainWindowMaximized;
    public bool IsMainWindowMaximized { get => _isMainWindowMaximized; set => SetField(ref _isMainWindowMaximized, value); }
}

public sealed class UserSettings : ObservableSettings
{
    private StreamingMaxBitratePreference _streamingMaxBitrateWifiPreference = StreamingMaxBitratePreference.NoLimit;
    public StreamingMaxBitratePreference StreamingMaxBitrateWifiPreference { get => _streamingMaxBitrateWifiPreference; set => SetField(ref _streamingMaxBitrateWifiPreference, value); }

    private StreamingMaxBitratePreference _streamingMaxBitrateCellularPreference = StreamingMaxBitratePreference.NoLimit;
    public StreamingMaxBitratePreference StreamingMaxBitrateCellularPreference { get => _streamingMaxBitrateCellularPreference; set => SetField(ref _streamingMaxBitrateCellularPreference, value); }

    private StreamingFormatPreference _streamingFormatWifiPreference = StreamingFormatPreference.Mp3;
    public StreamingFormatPreference StreamingFormatWifiPreference { get => _streamingFormatWifiPreference; set => SetField(ref _streamingFormatWifiPreference, value); }

    private StreamingFormatPreference _streamingFormatCellularPreference = StreamingFormatPreference.Mp3;
    public StreamingFormatPreference StreamingFormatCellularPreference { get => _streamingFormatCellularPreference; set => SetField(ref _streamingFormatCellularPreference, value); }

    private CacheTranscodingFormatPreference _cacheTranscodingFormatPreference = CacheTranscodingFormatPreference.Mp3;
    public CacheTranscodingFormatPreference CacheTranscodingFormatPreference { get => _cacheTranscodingFormatPreference; set => SetField(ref _cacheTranscodingFormatPreference, value); }

    private bool _isShowDetailedInfo;
    public bool IsShowDetailedInfo { get => _isShowDetailedInfo; set => SetField(ref _isShowDetailedInfo, value); }

    private bool _isShowSongDuration;
    public bool IsShowSongDuration { get => _isShowSongDuration; set => SetField(ref _isShowSongDuration, value); }

    private bool _isShowAlbumDuration;
    public bool IsShowAlbumDuration { get => _isShowAlbumDuration; set => SetField(ref _isShowAlbumDuration, value); }

    private bool _isShowArtistDuration;
    public bool IsShowArtistDuration { get => _isShowArtistDuration; set => SetField(ref _isShowArtistDuration, value); }

    private bool _isShowRating;
    public bool IsShowRating { get => _isShowRating; set => SetField(ref _isShowRating, value); }

    private bool _isPlayerShuffleButtonEnabled = true;
    public bool IsPlayerShuffleButtonEnabled { get => _isPlayerShuffleButtonEnabled; set => SetField(ref _isPlayerShuffleButtonEnabled, value); }

    private bool _isShowMusicPlayerSkipButtons;
    public bool IsShowMusicPlayerSkipButtons { get => _isShowMusicPlayerSkipButtons; set => SetField(ref _isShowMusicPlayerSkipButtons, value); }

    private bool _isLyricsSmoothScrolling = true;
    public bool IsLyricsSmoothScrolling { get => _isLyricsSmoothScrolling; set => SetField(ref _isLyricsSmoothScrolling, value); }

    /// cache limit in bytes (0 = no limit)
    private long _cacheLimit;
    public long CacheLimit { get => _cacheLimit; set => SetField(ref _cacheLimit, value); }

    private bool _isPlayerLyricsDisplayed;
    public bool IsPlayerLyricsDisplayed { get => _isPlayerLyricsDisplayed; set => SetField(ref _isPlayerLyricsDisplayed, value); }

    private bool _isPlayerVisualizerDisplayed;
    public bool IsPlayerVisualizerDisplayed { get => _isPlayerVisualizerDisplayed; set => SetField(ref _isPlayerVisualizerDisplayed, value); }

    private VisualizerType _selectedVisualizerType = VisualizerType.Ring;
    public VisualizerType SelectedVisualizerType { get => _selectedVisualizerType; set => SetField(ref _selectedVisualizerType, value); }

    private bool _isOfflineMode;
    public bool IsOfflineMode { get => _isOfflineMode; set => SetField(ref _isOfflineMode, value); }

    [JsonIgnore]
    public bool IsOnlineMode => !_isOfflineMode;

    private bool _isPlaybackStartOnlyOnPlay;
    public bool IsPlaybackStartOnlyOnPlay { get => _isPlaybackStartOnlyOnPlay; set => SetField(ref _isPlaybackStartOnlyOnPlay, value); }

    private bool _isPlayerSongPlaybackResumeEnabled;
    public bool IsPlayerSongPlaybackResumeEnabled { get => _isPlayerSongPlaybackResumeEnabled; set => SetField(ref _isPlayerSongPlaybackResumeEnabled, value); }

    private bool _isAutoplayEnabled;
    public bool IsAutoplayEnabled { get => _isAutoplayEnabled; set => SetField(ref _isAutoplayEnabled, value); }

    private bool _isEqualizerEnabled;
    public bool IsEqualizerEnabled { get => _isEqualizerEnabled; set => SetField(ref _isEqualizerEnabled, value); }

    private EqualizerSetting _activeEqualizerSetting = EqualizerSetting.Off;
    public EqualizerSetting ActiveEqualizerSetting { get => _activeEqualizerSetting; set => SetField(ref _activeEqualizerSetting, value); }

    private List<EqualizerSetting> _equalizerSettings = [];
    public List<EqualizerSetting> EqualizerSettings { get => _equalizerSettings; set => SetField(ref _equalizerSettings, value); }

    private bool _isReplayGainEnabled = true;
    public bool IsReplayGainEnabled { get => _isReplayGainEnabled; set => SetField(ref _isReplayGainEnabled, value); }

    private bool _isMiniPlayerAlwaysOnTop;
    public bool IsMiniPlayerAlwaysOnTop { get => _isMiniPlayerAlwaysOnTop; set => SetField(ref _isMiniPlayerAlwaysOnTop, value); }

    private float _playerVolume = 1.0f;
    public float PlayerVolume
    {
        get => _playerVolume is >= 0.0f and <= 1.0f ? _playerVolume : 1.0f;
        set { if (value is >= 0.0f and <= 1.0f) SetField(ref _playerVolume, value); }
    }

    private AppearanceMode _appearanceMode = AppearanceMode.System;
    public AppearanceMode AppearanceMode { get => _appearanceMode; set => SetField(ref _appearanceMode, value); }

    private ScreenLockPreventionPreference _screenLockPreventionPreference = ScreenLockPreventionPreference.Never;
    public ScreenLockPreventionPreference ScreenLockPreventionPreference { get => _screenLockPreventionPreference; set => SetField(ref _screenLockPreventionPreference, value); }

    private PlaylistSortType _playlistsSortSetting = PlaylistSortType.Name;
    public PlaylistSortType PlaylistsSortSetting { get => _playlistsSortSetting; set => SetField(ref _playlistsSortSetting, value); }

    private ArtistElementSortType _artistsSortSetting = ArtistElementSortType.Name;
    public ArtistElementSortType ArtistsSortSetting { get => _artistsSortSetting; set => SetField(ref _artistsSortSetting, value); }

    private AlbumElementSortType _albumsSortSetting = AlbumElementSortType.Name;
    public AlbumElementSortType AlbumsSortSetting { get => _albumsSortSetting; set => SetField(ref _albumsSortSetting, value); }

    private SongElementSortType _songsSortSetting = SongElementSortType.Name;
    public SongElementSortType SongsSortSetting
    {
        get => _songsSortSetting;
        set => SetField(ref _songsSortSetting, value == SongElementSortType.StarredDate ? SongElementSortType.Name : value);
    }

    private SongElementSortType _favoriteSongSortSetting = SongElementSortType.StarredDate;
    public SongElementSortType FavoriteSongSortSetting { get => _favoriteSongSortSetting; set => SetField(ref _favoriteSongSortSetting, value); }

    private ArtistCategoryFilter _artistsFilterSetting = ArtistCategoryFilter.AlbumArtists;
    public ArtistCategoryFilter ArtistsFilterSetting { get => _artistsFilterSetting; set => SetField(ref _artistsFilterSetting, value); }

    private AlbumsDisplayStyle _albumsStyleSetting = AlbumsDisplayStyle.Grid;
    public AlbumsDisplayStyle AlbumsStyleSetting { get => _albumsStyleSetting; set => SetField(ref _albumsStyleSetting, value); }

    private PodcastsShowType _podcastsShowSetting = PodcastsShowType.Podcasts;
    public PodcastsShowType PodcastsShowSetting { get => _podcastsShowSetting; set => SetField(ref _podcastsShowSetting, value); }

    private PlayerDisplayStyle _playerDisplayStyle = PlayerDisplayStyle.Large;
    public PlayerDisplayStyle PlayerDisplayStyle { get => _playerDisplayStyle; set => SetField(ref _playerDisplayStyle, value); }

    private int _albumsGridSizeSetting = 4;
    public int AlbumsGridSizeSetting { get => _albumsGridSizeSetting; set => SetField(ref _albumsGridSizeSetting, value); }

    // Windows specific
    private bool _isCloseToTray;
    public bool IsCloseToTray { get => _isCloseToTray; set => SetField(ref _isCloseToTray, value); }

    private bool _isDownloadNotificationsEnabled = false;
    public bool IsDownloadNotificationsEnabled { get => _isDownloadNotificationsEnabled; set => SetField(ref _isDownloadNotificationsEnabled, value); }
}

public sealed class LibraryDisplaySettings
{
    public List<LibraryDisplayType> InUse { get; set; } = [];

    [JsonIgnore]
    public List<LibraryDisplayType> NotUsed =>
        Enum.GetValues<LibraryDisplayType>().Where(t => !InUse.Contains(t)).OrderBy(t => (int)t).ToList();

    public LibraryDisplaySettings() { }
    public LibraryDisplaySettings(IEnumerable<LibraryDisplayType> inUse) { InUse = inUse.ToList(); }

    public bool IsVisible(LibraryDisplayType libraryType) => InUse.Contains(libraryType);

    public static LibraryDisplaySettings DefaultSettings => new([
        LibraryDisplayType.Artists, LibraryDisplayType.Albums, LibraryDisplayType.NewestAlbums,
        LibraryDisplayType.RecentAlbums, LibraryDisplayType.Songs, LibraryDisplayType.FavoriteSongs,
        LibraryDisplayType.Directories, LibraryDisplayType.Playlists, LibraryDisplayType.Podcasts, LibraryDisplayType.Radios,
    ]);

    public static LibraryDisplaySettings AddToPlaylistSettings => new([
        LibraryDisplayType.Genres, LibraryDisplayType.Artists, LibraryDisplayType.FavoriteArtists, LibraryDisplayType.Albums,
        LibraryDisplayType.FavoriteAlbums, LibraryDisplayType.NewestAlbums, LibraryDisplayType.RecentAlbums,
        LibraryDisplayType.Songs, LibraryDisplayType.FavoriteSongs, LibraryDisplayType.Directories, LibraryDisplayType.Playlists,
    ]);
}

public sealed class AccountSetting : ObservableSettings
{
    private ArtworkDisplayPreference _artworkDisplayPreference = ArtworkDisplayPreference.PreferId3Tag;
    public ArtworkDisplayPreference ArtworkDisplayPreference { get => _artworkDisplayPreference; set => SetField(ref _artworkDisplayPreference, value); }

    private ArtworkDownloadSetting _artworkDownloadSetting = ArtworkDownloadSetting.OnlyOnce;
    public ArtworkDownloadSetting ArtworkDownloadSetting { get => _artworkDownloadSetting; set => SetField(ref _artworkDownloadSetting, value); }

    /// ordered visible home sections
    private List<HomeSection> _homeSections = [.. SettingEnumerationExtensions.DefaultHomeSections];
    public List<HomeSection> HomeSections { get => _homeSections; set => SetField(ref _homeSections, value); }

    private SyncCompletionStatus _initialSyncCompletionStatus = SyncCompletionStatus.Completed;
    public SyncCompletionStatus InitialSyncCompletionStatus { get => _initialSyncCompletionStatus; set => SetField(ref _initialSyncCompletionStatus, value); }

    private bool _isAutoDownloadLatestPodcastEpisodesActive;
    public bool IsAutoDownloadLatestPodcastEpisodesActive { get => _isAutoDownloadLatestPodcastEpisodesActive; set => SetField(ref _isAutoDownloadLatestPodcastEpisodesActive, value); }

    private bool _isAutoDownloadLatestSongsActive;
    public bool IsAutoDownloadLatestSongsActive { get => _isAutoDownloadLatestSongsActive; set => SetField(ref _isAutoDownloadLatestSongsActive, value); }

    private bool _isScrobbleStreamedItems;
    public bool IsScrobbleStreamedItems { get => _isScrobbleStreamedItems; set => SetField(ref _isScrobbleStreamedItems, value); }

    private LibraryDisplaySettings _libraryDisplaySettings = LibraryDisplaySettings.DefaultSettings;
    public LibraryDisplaySettings LibraryDisplaySettings { get => _libraryDisplaySettings; set => SetField(ref _libraryDisplaySettings, value); }

    private LoginCredentials? _loginCredentials;
    public LoginCredentials? LoginCredentials { get => _loginCredentials; set => SetField(ref _loginCredentials, value); }

    private ThemePreference _themePreference = ThemePreference.Blue;
    public ThemePreference ThemePreference { get => _themePreference; set => SetField(ref _themePreference, value); }
}

public sealed class AccountSettings : ObservableSettings
{
    /// Account ident -> setting
    public Dictionary<string, AccountSetting> Accounts { get; set; } = [];

    public string? ActiveAccountIdent { get; set; }

    [JsonIgnore]
    public AccountInfo? Active => ActiveAccountIdent is null ? null : AccountInfo.Create(ActiveAccountIdent);

    public override void OnDeserialized()
    {
        foreach (var s in Accounts.Values) AttachChild(s);
    }

    public void SwitchActiveAccount(AccountInfo accountInfo)
    {
        ActiveAccountIdent = accountInfo.Ident;
        NotifyChanged();
    }

    public void Login(LoginCredentials loginCredentials)
    {
        var info = AccountInfo.Create(loginCredentials);
        var newTheme = ThemeColorForNextNewAccount;
        UpdateSetting(info, s =>
        {
            s.LoginCredentials = loginCredentials;
            s.ThemePreference = newTheme;
        });
        ActiveAccountIdent = info.Ident;
        NotifyChanged();
    }

    /// Settings priority: 1. provided account, 2. active account, 3. defaults (detached).
    public AccountSetting GetSetting(AccountInfo? accountInfo)
    {
        var ident = accountInfo?.Ident ?? ActiveAccountIdent;
        if (ident is not null && Accounts.TryGetValue(ident, out var s)) return s;
        return new AccountSetting();
    }

    [JsonIgnore]
    public AccountSetting ActiveSetting => GetSetting(null);

    public void Logout(AccountInfo accountInfo)
    {
        if (Accounts.Remove(accountInfo.Ident, out var removed)) removed.Changed -= NotifyChanged;
        if (ActiveAccountIdent == accountInfo.Ident)
            ActiveAccountIdent = Accounts.Keys.FirstOrDefault();
        NotifyChanged();
    }

    public void UpdateSetting(AccountInfo accountInfo, Action<AccountSetting> update)
    {
        if (!Accounts.TryGetValue(accountInfo.Ident, out var setting))
        {
            setting = new AccountSetting();
            Accounts[accountInfo.Ident] = setting;
            AttachChild(setting);
        }
        update(setting);
        NotifyChanged();
    }

    [JsonIgnore]
    public IReadOnlySet<ServerApiType> AvailableApiTypes =>
        Accounts.Values.Select(a => a.LoginCredentials?.BackendApi.AsServerApiType()).OfType<ServerApiType>().ToHashSet();

    [JsonIgnore]
    public List<AccountInfo> AllAccounts =>
        Accounts.Keys.Select(k => AccountInfo.Create(k)!)
            .OrderBy(a => GetSetting(a).LoginCredentials?.DisplayServerUrl ?? "", StringComparer.Ordinal)
            .ThenBy(a => GetSetting(a).LoginCredentials?.Username ?? "", StringComparer.Ordinal)
            .ToList();

    private ThemePreference ThemeColorForNextNewAccount
    {
        get
        {
            var used = Accounts.Values.Select(a => a.ThemePreference).ToHashSet();
            var available = Enum.GetValues<ThemePreference>().Where(t => !used.Contains(t)).OrderBy(t => (int)t).ToList();
            if (available.Count > 0) return available[0];
            if (Active is { } active)
            {
                var activeTheme = GetSetting(active).ThemePreference;
                return Enum.GetValues<ThemePreference>().Where(t => t != activeTheme).OrderBy(t => (int)t).FirstOrDefault(ThemePreference.Blue);
            }
            return ThemePreference.Blue;
        }
    }
}

/// All persistent settings. Stored as JSON (settings.json). Every change is persisted automatically (debounced).
public sealed class AmperfySettings : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string? _filePath;
    private readonly object _saveLock = new();
    private Timer? _saveTimer;

    public AppSettings App { get; private set; } = new();
    public UserSettings User { get; private set; } = new();
    public AccountSettings Accounts { get; private set; } = new();

    public event Action? SettingsChanged;

    private sealed class Snapshot
    {
        public AppSettings? App { get; set; }
        public UserSettings? User { get; set; }
        public AccountSettings? Accounts { get; set; }
    }

    /// In-memory settings (tests).
    public AmperfySettings() => Hook();

    public AmperfySettings(string filePath)
    {
        _filePath = filePath;
        if (File.Exists(filePath))
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(filePath), JsonOptions);
                App = snapshot?.App ?? new();
                User = snapshot?.User ?? new();
                Accounts = snapshot?.Accounts ?? new();
            }
            catch (Exception ex)
            {
                AmperfyLog.Error("Settings", $"Failed to read settings: {ex.Message}");
            }
        }
        Hook();
    }

    private void Hook()
    {
        App.Changed += OnChanged;
        User.Changed += OnChanged;
        Accounts.Changed += OnChanged;
    }

    private void OnChanged()
    {
        SettingsChanged?.Invoke();
        if (_filePath is null) return;
        _saveTimer ??= new Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);
        _saveTimer.Change(300, Timeout.Infinite);
    }

    public void SaveNow()
    {
        if (_filePath is null) return;
        lock (_saveLock)
        {
            try
            {
                var json = JsonSerializer.Serialize(new Snapshot { App = App, User = User, Accounts = Accounts }, JsonOptions);
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                var tmp = _filePath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, _filePath, overwrite: true);
            }
            catch (Exception ex)
            {
                AmperfyLog.Error("Settings", $"Failed to save settings: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        _saveTimer?.Dispose();
        SaveNow();
    }
}
