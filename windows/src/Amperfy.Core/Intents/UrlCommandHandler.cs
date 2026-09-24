using Amperfy.Core.Api;
using Amperfy.Core.Player;

namespace Amperfy.Core.Intents;

public sealed record UrlCommandParameterDocu(string Name, string Type, bool IsMandatory, string Description, string? DefaultIfNotGiven = null);

public sealed record UrlCommandDocu(string Name, string Description, IReadOnlyList<string> ExampleUrls, string Action,
    IReadOnlyList<UrlCommandParameterDocu> Parameters);

/// Result of an URL command. <see cref="CallbackUrl"/> is the x-success / x-error callback the
/// caller asked for (the app opens it).
public sealed record UrlCommandResult(bool Success, string? ErrorMessage, Uri? CallbackUrl);

/// Port of the x-callback-url part of IntentManager.swift:
/// amperfy://x-callback-url/[action]?[x-callback parameters]&amp;[action parameters]
public sealed class UrlCommandHandler
{
    public const string Scheme = "amperfy";
    public const string Host = "x-callback-url";

    private const string SearchTerm = "searchTerm";
    private const string SearchCategory = "searchCategory";
    private const string ShuffleOption = "shuffleOption";
    private const string RepeatOption = "repeatOption";
    private const string IdKey = "id";
    private const string LibraryElementType = "libraryElementType";
    private const string OnlyCached = "onlyCached";
    private const string OfflineMode = "offlineMode";
    private const string Rating = "rating";
    private const string Favorite = "favorite";

    private const string ContainerTypesDescription = "artist, song, podcastEpisode, playlist, album, genre, podcast, radio";

    private readonly LibraryStorage _library;
    private readonly AmperfySettings _settings;
    private readonly IPlayerFacade _player;
    private readonly INetworkMonitor _networkMonitor;
    private readonly Func<Account?> _getActiveAccount;
    private readonly Func<AccountInfo, ILibrarySyncer> _getLibrarySyncer;

    public UrlCommandHandler(LibraryStorage library, AmperfySettings settings, IPlayerFacade player, INetworkMonitor networkMonitor,
        Func<Account?> getActiveAccount, Func<AccountInfo, ILibrarySyncer> getLibrarySyncer)
    {
        _library = library;
        _settings = settings;
        _player = player;
        _networkMonitor = networkMonitor;
        _getActiveAccount = getActiveAccount;
        _getLibrarySyncer = getLibrarySyncer;
        Documentation = BuildDocumentation(player.MaxSongsToAddOnce);
    }

    public IReadOnlyList<UrlCommandDocu> Documentation { get; }

    public static bool IsCommandUrl(Uri url) =>
        url.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase) && url.Host.Equals(Host, StringComparison.OrdinalIgnoreCase);

    public static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf('=');
            var key = Uri.UnescapeDataString((idx < 0 ? part : part[..idx]).Replace('+', ' '));
            var value = idx < 0 ? "" : Uri.UnescapeDataString(part[(idx + 1)..].Replace('+', ' '));
            result.TryAdd(key, value);
        }
        return result;
    }

    public async Task<UrlCommandResult> HandleAsync(Uri url)
    {
        if (!IsCommandUrl(url)) return new UrlCommandResult(false, "Unsupported URL.", null);
        var action = url.AbsolutePath.Trim('/');
        var parameters = ParseQuery(url.Query);
        string? error;
        try
        {
            error = await PerformAsync(action, parameters);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        AmperfyLog.Info("UrlCommandHandler", $"{action}: {(error is null ? "success" : error)}");
        var callbackKey = error is null ? "x-success" : "x-error";
        Uri? callback = null;
        if (parameters.TryGetValue(callbackKey, out var cb) && Uri.TryCreate(cb, UriKind.Absolute, out var cbUri))
        {
            callback = error is null ? cbUri : AppendQuery(cbUri, "errorMessage", error);
        }
        return new UrlCommandResult(error is null, error, callback);
    }

    private static Uri AppendQuery(Uri uri, string key, string value)
    {
        var separator = string.IsNullOrEmpty(uri.Query) ? "?" : "&";
        return new Uri(uri + separator + Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value));
    }

    private static bool TryGetBool01(Dictionary<string, string> p, string key, out bool value)
    {
        value = false;
        if (!p.TryGetValue(key, out var raw) || !int.TryParse(raw, out var i) || i is < 0 or > 1) return false;
        value = i == 1;
        return true;
    }

    private static bool TryGetRepeat(Dictionary<string, string> p, out RepeatMode mode)
    {
        mode = RepeatMode.Off;
        if (!p.TryGetValue(RepeatOption, out var raw) || !short.TryParse(raw, out var i) || !Enum.IsDefined((RepeatMode)i)) return false;
        mode = (RepeatMode)i;
        return true;
    }

    /// Returns null on success, otherwise the error message.
    private async Task<string?> PerformAsync(string action, Dictionary<string, string> p)
    {
        switch (action)
        {
            case "searchAndPlay":
            {
                if (_getActiveAccount() is not { } account) return "You are not yet logged in.";
                if (!p.TryGetValue(SearchTerm, out var term)) return "Parameter searchTerm not provided.";
                if (!p.TryGetValue(SearchCategory, out var categoryRaw)) return "Parameter searchCategory not provided.";
                if (ParseContainerType(categoryRaw) is not { } category) return "Parameter searchCategory is not valid.";
                TryGetBool01(p, ShuffleOption, out var shuffle);
                TryGetRepeat(p, out var repeat);
                var container = FindContainer(account, term, category);
                return await PlayAsync(container, shuffle, repeat) ? null : "Requested element could not be played.";
            }
            case "playID":
            {
                if (_getActiveAccount() is not { } account) return "You are not yet logged in.";
                if (!p.TryGetValue(IdKey, out var id)) return "Parameter id not provided.";
                if (!p.TryGetValue(LibraryElementType, out var typeRaw)) return "Parameter libraryElementType not provided.";
                if (ParseContainerType(typeRaw) is not { } type) return "Parameter libraryElementType is not valid.";
                TryGetBool01(p, ShuffleOption, out var shuffle);
                TryGetRepeat(p, out var repeat);
                var container = GetContainer(account, id, type);
                return await PlayAsync(container, shuffle, repeat) ? null : "Requested element could not be played.";
            }
            case "playRandomSongs":
            {
                if (_getActiveAccount() is not { } account) return "You are not yet logged in.";
                TryGetBool01(p, OnlyCached, out var onlyCached);
                var songs = _library.GetRandomSongs(account, _player.MaxSongsToAddOnce, onlyCached);
                _player.Play(new PlayContext("Random Songs", songs.Cast<AbstractPlayable>().ToList()));
                return null;
            }
            case "play":
                _player.Play();
                return null;
            case "pause":
                _player.Pause();
                return null;
            case "togglePlayPause":
                _player.TogglePlayPause();
                return null;
            case "playNext":
                _player.PlayNext();
                return null;
            case "playPreviousOrReplay":
                _player.PlayPreviousOrReplay();
                return null;
            case "setShuffle":
            {
                if (!p.ContainsKey(ShuffleOption)) return "Parameter shuffleOption not provided.";
                if (!TryGetBool01(p, ShuffleOption, out var shuffle)) return "Parameter shuffleOption is not valid.";
                if (_player.IsShuffle != shuffle) _player.ToggleShuffle();
                return null;
            }
            case "setRepeat":
            {
                if (!p.ContainsKey(RepeatOption)) return "Parameter repeatOption not provided.";
                if (!TryGetRepeat(p, out var repeat)) return "Parameter repeatOption is not valid.";
                _player.SetRepeatMode(repeat);
                return null;
            }
            case "setOfflineMode":
            {
                if (!p.ContainsKey(OfflineMode)) return "Parameter offlineMode not provided.";
                if (!TryGetBool01(p, OfflineMode, out var offline)) return "Parameter offlineMode is not valid.";
                _settings.User.IsOfflineMode = offline;
                return null;
            }
            case "rateCurrentlyPlayingSong":
            {
                if (!p.TryGetValue(Rating, out var raw)) return "Parameter rating not provided.";
                if (!int.TryParse(raw, out var rating) || rating is < 0 or > 5) return "Parameter rating is not valid. Must be between 0 and 5.";
                if (!_settings.User.IsOnlineMode) return "Rating can only be changed in Online Mode.";
                if (_player.CurrentlyPlaying is not { } playing) return "There is no song currently playing.";
                if (playing is not Song song || song.Account is not { } account) return "Only songs can be rated.";
                song.Rating = rating;
                _library.SaveContext();
                await _getLibrarySyncer(account.Info).SetRatingAsync(song, rating);
                return null;
            }
            case "favoriteCurrentlyPlayingSong":
            {
                if (!p.ContainsKey(Favorite)) return "Parameter favorite not provided.";
                if (!TryGetBool01(p, Favorite, out var favorite)) return "Parameter favorite is not valid.";
                if (!_settings.User.IsOnlineMode) return "Favorite can only be changed in Online Mode.";
                if (_player.CurrentlyPlaying is not { } playing) return "There is no song currently playing.";
                if (playing is not Song song || song.Account is not { } account) return "Only songs can be rated.";
                if (song.IsFavorite != favorite) await song.RemoteToggleFavoriteAsync(_library, _getLibrarySyncer(account.Info));
                return null;
            }
            default:
                return $"Unknown action '{action}'.";
        }
    }

    public static PlayableContainerBaseType? ParseContainerType(string raw) => raw switch
    {
        "artist" => PlayableContainerBaseType.Artist,
        "song" => PlayableContainerBaseType.Song,
        "podcastEpisode" => PlayableContainerBaseType.PodcastEpisode,
        "playlist" => PlayableContainerBaseType.Playlist,
        "album" => PlayableContainerBaseType.Album,
        "genre" => PlayableContainerBaseType.Genre,
        "podcast" => PlayableContainerBaseType.Podcast,
        "radio" => PlayableContainerBaseType.Radio,
        _ => null,
    };

    public IPlayableContainable? FindContainer(Account account, string searchTerm, PlayableContainerBaseType category)
    {
        static IPlayableContainable? Best<T>(IEnumerable<T> items, string search) where T : IPlayableContainable =>
            FuzzySearcher.FindBestMatch(items, i => i.Name, search).FirstOrDefault();
        return category switch
        {
            PlayableContainerBaseType.Song => Best(_library.GetSongs(account), searchTerm),
            PlayableContainerBaseType.Artist => Best(_library.GetArtists(account), searchTerm),
            PlayableContainerBaseType.PodcastEpisode => Best(_library.GetPodcastEpisodes(account), searchTerm),
            PlayableContainerBaseType.Playlist => Best(_library.GetPlaylists(account), searchTerm),
            PlayableContainerBaseType.Album => Best(_library.GetAlbums(account), searchTerm),
            PlayableContainerBaseType.Genre => Best(_library.GetGenres(account), searchTerm),
            PlayableContainerBaseType.Podcast => Best(_library.GetPodcasts(account), searchTerm),
            PlayableContainerBaseType.Radio => Best(_library.GetRadios(account), searchTerm),
            _ => null,
        };
    }

    public IPlayableContainable? GetContainer(Account account, string id, PlayableContainerBaseType type)
    {
        var api = account.ApiType.AsServerApiType();
        return type switch
        {
            PlayableContainerBaseType.Song => _library.GetSong(account, id),
            PlayableContainerBaseType.Artist => _library.GetArtist(account, id) ??
                                                (api == ServerApiType.Subsonic ? _library.GetArtistLocal(account, id) : null),
            PlayableContainerBaseType.PodcastEpisode => _library.GetPodcastEpisode(account, id),
            PlayableContainerBaseType.Playlist => _library.GetPlaylist(account, id),
            PlayableContainerBaseType.Album => _library.GetAlbum(account, id),
            PlayableContainerBaseType.Genre => api == ServerApiType.Ampache ? _library.GetGenre(account, id) : _library.GetGenreByName(account, id),
            PlayableContainerBaseType.Podcast => _library.GetPodcast(account, id),
            PlayableContainerBaseType.Radio => _library.GetRadio(account, id),
            _ => null,
        };
    }

    private async Task<bool> PlayAsync(IPlayableContainable? container, bool shuffle, RepeatMode repeat)
    {
        if (container is null) return false;
        if (container is Playlist playlist && playlist.Account is { } account &&
            _settings.User.IsOnlineMode && _networkMonitor.IsWifiOrEthernet)
        {
            try
            {
                await container.FetchAsync(_settings, _getLibrarySyncer(account.Info));
            }
            catch (Exception ex)
            {
                AmperfyLog.Error("UrlCommandHandler", $"Fetch failed: {ex.Message}");
            }
        }
        var context = new PlayContext(container);
        if (context.Playables.Count == 0) return false;
        if (shuffle) _player.PlayShuffled(context);
        else _player.Play(context);
        _player.SetRepeatMode(repeat);
        return true;
    }

    private static List<UrlCommandDocu> BuildDocumentation(int maxSongs)
    {
        var shuffle = new UrlCommandParameterDocu(ShuffleOption, "Int", false, "0 (false) or 1 (true)", "false");
        var repeat = new UrlCommandParameterDocu(RepeatOption, "Int", false, "0 (off), 1 (all), 2 (single)", "off");
        UrlCommandDocu Simple(string name, string action, string description) =>
            new(name, description, [$"amperfy://x-callback-url/{action}"], action, []);
        return
        [
            new("SearchAndPlay", "Plays the first search result for searchTerm in searchCategory from the currently active account with the given player options",
                ["amperfy://x-callback-url/searchAndPlay?searchTerm=Awesome&searchCategory=playlist",
                 "amperfy://x-callback-url/searchAndPlay?searchTerm=Example&searchCategory=artist&shuffleOption=1&repeatOption=2"],
                "searchAndPlay",
                [new(SearchTerm, "String", true, "Query term to search for"),
                 new(SearchCategory, "String", true, ContainerTypesDescription), shuffle, repeat]),
            new("PlayID", "Plays the library element with the given ID from the currently active account with the provided player options",
                ["amperfy://x-callback-url/playID?id=123456&libraryElementType=playlist",
                 "amperfy://x-callback-url/playID?id=aa2349&libraryElementType=artist&shuffleOption=1&repeatOption=2"],
                "playID",
                [new(IdKey, "String", true, "ID of the library element"),
                 new(LibraryElementType, "String", true, ContainerTypesDescription), shuffle, repeat]),
            new("PlayRandomSongs", $"Plays {maxSongs} random songs from the currently active account",
                ["amperfy://x-callback-url/playRandomSongs", "amperfy://x-callback-url/playRandomSongs?onlyCached=1"],
                "playRandomSongs",
                [new(OnlyCached, "Int", false, "0 (false) or 1 (true), use only cached songs from library", "false")]),
            Simple("Play", "play", "Changes the play state of the player to play"),
            Simple("Pause", "pause", "Changes the play state of the player to pause"),
            Simple("TogglePlayPause", "togglePlayPause", "Toggles the play state of the player (play/pause)"),
            Simple("PlayNext", "playNext", "The next track will be played"),
            Simple("PlayPreviousOrReplay", "playPreviousOrReplay",
                $"The previous track will be played (if the tracked plays longer than {AudioPlayer.ReplayInsteadPlayPreviousTimeInSec} seconds the track starts from the beginning)"),
            new("SetShuffle", "Sets the shuffle state of the player", ["amperfy://x-callback-url/setShuffle?shuffleOption=1"], "setShuffle",
                [shuffle with { IsMandatory = true, DefaultIfNotGiven = null }]),
            new("SetRepeat", "Sets the repeat state of the player", ["amperfy://x-callback-url/setRepeat?repeatOption=2"], "setRepeat",
                [repeat with { IsMandatory = true, DefaultIfNotGiven = null }]),
            new("SetOfflineMode", "Sets the Amperfy offline mode to active/inactive", ["amperfy://x-callback-url/setOfflineMode?offlineMode=1"],
                "setOfflineMode", [new(OfflineMode, "Int", true, "0 (inactive) or 1 (active)")]),
            new("RateCurrentlyPlayingSong", "Rate the currently playing song.",
                ["amperfy://x-callback-url/rateCurrentlyPlayingSong?rating=0", "amperfy://x-callback-url/rateCurrentlyPlayingSong?rating=5"],
                "rateCurrentlyPlayingSong", [new(Rating, "Int", true, "Rating must be between 0 and 5")]),
            new("FavoriteCurrentlyPlayingSong", "Mark the currently playing song as favorite.",
                ["amperfy://x-callback-url/favoriteCurrentlyPlayingSong?favorite=0", "amperfy://x-callback-url/favoriteCurrentlyPlayingSong?favorite=1"],
                "favoriteCurrentlyPlayingSong", [new(Favorite, "Int", true, "0 (no favorite) or 1 (favorite)")]),
        ];
    }
}
