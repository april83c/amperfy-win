namespace Amperfy.Core.Sync;

/// Merges library entities which exist more than once with the same server id
/// (port of Storage/DuplicateEntitiesResolver.swift). Runs on the main thread.
public sealed class DuplicateEntitiesResolver
{
    private const string LogCategory = "DuplicateEntitiesResolver";
    private readonly Account _account;
    private readonly LibraryStorage _library;
    private bool _isRunning;
    private Task _runningTask = Task.CompletedTask;

    public DuplicateEntitiesResolver(Account account, LibraryStorage library)
    {
        _account = account;
        _library = library;
    }

    public bool IsActive { get; private set; }

    /// Completes when the current resolve run has finished.
    public Task RunningTask => _runningTask;

    public void Start()
    {
        _isRunning = true;
        if (IsActive) return;
        IsActive = true;
        _runningTask = ResolveDuplicatesInBackgroundAsync();
    }

    public void Stop() => _isRunning = false;

    private async Task ResolveDuplicatesInBackgroundAsync()
    {
        try
        {
            // let the caller continue first (Swift: Task { @MainActor in ... })
            await Task.Yield();
            AmperfyLog.Info(LogCategory, "start");
            ResolveAll();
            AmperfyLog.Info(LogCategory, "stopped");
        }
        catch (Exception ex)
        {
            AmperfyLog.Error(LogCategory, $"Resolving duplicates failed: {ex}");
        }
        finally
        {
            IsActive = false;
        }
    }

    /// Resolves all duplicates synchronously.
    public void ResolveAll()
    {
        // only check for genre duplicates by id on Ampache API, Subsonic does not have genre ids
        var serverApiType = _account.ApiType.AsServerApiType();
        if (_isRunning && serverApiType == ServerApiType.Ampache)
        {
            Step(() => _library.ResolveGenresDuplicates(_account, _library.FindDuplicates(DuplicateEntityType.GenreById, _account), byName: false));
        }
        else if (_isRunning && serverApiType == ServerApiType.Subsonic)
        {
            Step(() => _library.ResolveGenresDuplicates(_account, _library.FindDuplicates(DuplicateEntityType.GenreByName, _account), byName: true));
        }
        if (_isRunning) Step(() => _library.ResolveArtistsDuplicates(_account, _library.FindDuplicates(DuplicateEntityType.Artist, _account)));
        if (_isRunning) Step(() => _library.ResolveAlbumsDuplicates(_account, _library.FindDuplicates(DuplicateEntityType.Album, _account)));
        if (_isRunning) Step(() => _library.ResolveSongsDuplicates(_account, _library.FindDuplicates(DuplicateEntityType.Song, _account)));
        if (_isRunning) Step(() => _library.ResolvePodcastEpisodesDuplicates(_account, _library.FindDuplicates(DuplicateEntityType.PodcastEpisode, _account)));
        if (_isRunning) Step(() => _library.ResolveRadioDuplicates(_account, _library.FindDuplicates(DuplicateEntityType.Radio, _account)));
        if (_isRunning) Step(() => _library.ResolvePodcastsDuplicates(_account, _library.FindDuplicates(DuplicateEntityType.Podcast, _account)));
        if (_isRunning) Step(() => _library.ResolvePlaylistsDuplicates(_account, _library.FindDuplicates(DuplicateEntityType.Playlist, _account)));
    }

    private void Step(Action action)
    {
        // Swift: try? storage.async.perform { ... } (errors ignored, context saved)
        try
        {
            _library.SaveContext();
            action();
            _library.SaveContext();
        }
        catch (Exception ex)
        {
            AmperfyLog.Error(LogCategory, $"Resolve step failed: {ex.Message}");
        }
    }
}
