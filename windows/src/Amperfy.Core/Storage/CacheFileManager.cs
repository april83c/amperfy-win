namespace Amperfy.Core.Storage;

public static class MimeFileConverter
{
    public const string FilenameExtensionUnknown = "unknown";
    public const string MimeTypeUnknown = "application/octet-stream";

    private static readonly Dictionary<string, string> ExtensionToMime = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mp3"] = "audio/mpeg",
        ["m4a"] = "audio/mp4",
        ["mp4"] = "audio/mp4",
        ["aac"] = "audio/aac",
        ["flac"] = "audio/x-flac",
        ["ogg"] = "audio/ogg",
        ["oga"] = "audio/ogg",
        ["ogx"] = "application/ogg",
        ["opus"] = "audio/ogg",
        ["wav"] = "audio/wav",
        ["wma"] = "audio/x-ms-wma",
        ["aif"] = "audio/aiff",
        ["aiff"] = "audio/aiff",
        ["alac"] = "audio/mp4",
        ["webm"] = "audio/webm",
        ["png"] = "image/png",
        ["jpg"] = "image/jpeg",
        ["jpeg"] = "image/jpeg",
        ["gif"] = "image/gif",
        ["webp"] = "image/webp",
        ["xml"] = "application/xml",
    };

    private static readonly Dictionary<string, string> MimeToExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        ["audio/mpeg"] = "mp3",
        ["audio/mp3"] = "mp3",
        ["audio/mpeg3"] = "mp3",
        ["audio/x-mpeg-3"] = "mp3",
        ["audio/mp4"] = "m4a",
        ["audio/m4a"] = "m4a",
        ["audio/x-m4a"] = "m4a",
        ["audio/aac"] = "aac",
        ["audio/aacp"] = "aac",
        ["audio/flac"] = "flac",
        ["audio/x-flac"] = "flac",
        ["audio/ogg"] = "ogg",
        ["application/ogg"] = "ogx",
        ["audio/opus"] = "opus",
        ["audio/wav"] = "wav",
        ["audio/x-wav"] = "wav",
        ["audio/wave"] = "wav",
        ["audio/x-ms-wma"] = "wma",
        ["audio/aiff"] = "aiff",
        ["audio/x-aiff"] = "aiff",
        ["audio/webm"] = "webm",
        ["image/png"] = "png",
        ["image/jpeg"] = "jpg",
        ["image/gif"] = "gif",
        ["image/webp"] = "webp",
    };

    /// Types Windows Media Foundation can't decode out of the box.
    private static readonly HashSet<string> IncompatibleMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        MimeTypeUnknown,
    };

    private static readonly Dictionary<string, string> ConversionNeededMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["audio/x-flac"] = "audio/flac",
        ["audio/m4a"] = "audio/mp4",
    };

    public static string ConvertToValidMimeTypeWhenNecessary(string mimeType)
    {
        var lower = mimeType.ToLowerInvariant();
        return ConversionNeededMimeTypes.TryGetValue(lower, out var converted) ? converted : lower;
    }

    public static bool IsMimeTypePlayable(string mimeType) => !IncompatibleMimeTypes.Contains(mimeType);

    public static string? GetMimeType(string? filenameExtension)
    {
        if (string.IsNullOrEmpty(filenameExtension)) return null;
        var ext = filenameExtension.TrimStart('.').ToLowerInvariant();
        if (ext == "raw") return null;
        return ExtensionToMime.TryGetValue(ext, out var mime) ? mime : MimeTypeUnknown;
    }

    public static string GetFilenameExtension(string? mimeType)
    {
        if (string.IsNullOrEmpty(mimeType)) return FilenameExtensionUnknown;
        var mime = mimeType.Split(';')[0].Trim();
        return MimeToExtension.TryGetValue(mime, out var ext) ? ext : FilenameExtensionUnknown;
    }
}

/// Manages Amperfy's file cache (downloaded songs/episodes, artworks, lyrics). Paths stored in the
/// database are relative to <see cref="RootDirectory"/> and use '/' as separator.
public sealed class CacheFileManager
{
    private static CacheFileManager? _shared;

    /// Global instance (set up by the app on startup; tests may replace it).
    public static CacheFileManager Shared
    {
        get => _shared ??= new CacheFileManager(Path.Combine(Path.GetTempPath(), "Amperfy"));
        set => _shared = value;
    }

    public const string AccountsDir = "accounts";
    public const string SongsDir = "songs";
    public const string EpisodesDir = "episodes";
    public const string ArtworksDir = "artworks";
    public const string EmbeddedArtworksDir = "embedded-artworks";
    public const string LyricsDir = "lyrics";
    public const string ArtworkFileExtension = "png";
    public const string LyricsFileExtension = "xml";

    public string RootDirectory { get; }

    private readonly object _sizeLock = new();
    private long _completePlayableCacheSize;
    private readonly Dictionary<AccountInfo, long> _accountPlayableCacheSize = [];

    public CacheFileManager(string rootDirectory)
    {
        RootDirectory = rootDirectory;
        Directory.CreateDirectory(rootDirectory);
    }

    public long CompletePlayableCacheSize { get { lock (_sizeLock) return _completePlayableCacheSize; } }

    public long GetPlayableCacheSize(AccountInfo accountInfo)
    {
        lock (_sizeLock) return _accountPlayableCacheSize.GetValueOrDefault(accountInfo);
    }

    public void RecalculatePlayableCacheSizes()
    {
        long complete = 0;
        var sizes = new Dictionary<AccountInfo, long>();
        foreach (var account in GetAccounts())
        {
            var size = DirectorySize(GetAbsolutePath(GetRelSongsDirectory(account))) + DirectorySize(GetAbsolutePath(GetRelPodcastEpisodesDirectory(account)));
            sizes[account] = size;
            complete += size;
        }
        lock (_sizeLock)
        {
            _accountPlayableCacheSize.Clear();
            foreach (var (k, v) in sizes) _accountPlayableCacheSize[k] = v;
            _completePlayableCacheSize = complete;
        }
    }

    // --- relative paths ------------------------------------------------------------------------

    public static string GetRelPath(AccountInfo account) => $"{AccountsDir}/{account.ServerHash}/{account.UserHash}";
    public static string GetRelSongsDirectory(AccountInfo account) => $"{GetRelPath(account)}/{SongsDir}";
    public static string GetRelPodcastEpisodesDirectory(AccountInfo account) => $"{GetRelPath(account)}/{EpisodesDir}";
    public static string GetRelArtworkDirectory(AccountInfo account) => $"{GetRelPath(account)}/{ArtworksDir}";
    public static string GetRelEmbeddedArtworkDirectory(AccountInfo account) => $"{GetRelPath(account)}/{EmbeddedArtworksDir}";
    public static string GetRelLyricsDirectory(AccountInfo account) => $"{GetRelPath(account)}/{LyricsDir}";

    /// Makes a file name safe for the Windows file system.
    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*']).ToHashSet();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name) sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.ToString();
    }

    public static string GetRelSongFilePath(AccountInfo account, string songId, string? mimeType) =>
        $"{GetRelSongsDirectory(account)}/{SanitizeFileName(songId)}.{MimeFileConverter.GetFilenameExtension(mimeType)}";

    public static string GetRelEpisodeFilePath(AccountInfo account, string episodeId, string? mimeType) =>
        $"{GetRelPodcastEpisodesDirectory(account)}/{SanitizeFileName(episodeId)}.{MimeFileConverter.GetFilenameExtension(mimeType)}";

    public static string GetRelArtworkFilePath(AccountInfo account, string artworkId, string artworkType) =>
        string.IsNullOrEmpty(artworkType)
            ? $"{GetRelArtworkDirectory(account)}/{SanitizeFileName(artworkId)}.{ArtworkFileExtension}"
            : $"{GetRelArtworkDirectory(account)}/{SanitizeFileName(artworkType)}/{SanitizeFileName(artworkId)}.{ArtworkFileExtension}";

    public static string GetRelEmbeddedArtworkFilePath(AccountInfo account, string playableId, bool isSong) =>
        $"{GetRelEmbeddedArtworkDirectory(account)}/{(isSong ? SongsDir : EpisodesDir)}/{SanitizeFileName(playableId)}.{ArtworkFileExtension}";

    public static string GetRelLyricsFilePath(AccountInfo account, string songId) =>
        $"{GetRelLyricsDirectory(account)}/{SongsDir}/{SanitizeFileName(songId)}.{LyricsFileExtension}";

    // --- absolute paths ------------------------------------------------------------------------

    public string GetAbsolutePath(string relPath) => Path.Combine(RootDirectory, relPath.Replace('/', Path.DirectorySeparatorChar));

    /// Absolute path for a relative path (null if it does not exist).
    public string? GetAbsoluteAmperfyPath(string relFilePath)
    {
        var abs = GetAbsolutePath(relFilePath);
        return abs;
    }

    public bool FileExists(string relFilePath) => File.Exists(GetAbsolutePath(relFilePath));

    public long? GetFileSize(string absPath)
    {
        try { return new FileInfo(absPath).Length; } catch { return null; }
    }

    public string GetOrCreateAbsoluteDirectory(string relDir)
    {
        var abs = GetAbsolutePath(relDir);
        Directory.CreateDirectory(abs);
        return abs;
    }

    public string GetOrCreateAbsoluteAccountDirectory(AccountInfo account) => GetOrCreateAbsoluteDirectory(GetRelPath(account));
    public string GetOrCreateAbsoluteSongsDirectory(AccountInfo account) => GetOrCreateAbsoluteDirectory(GetRelSongsDirectory(account));
    public string GetOrCreateAbsolutePodcastEpisodesDirectory(AccountInfo account) => GetOrCreateAbsoluteDirectory(GetRelPodcastEpisodesDirectory(account));
    public string GetOrCreateAbsoluteArtworksDirectory(AccountInfo account) => GetOrCreateAbsoluteDirectory(GetRelArtworkDirectory(account));
    public string GetOrCreateAbsoluteEmbeddedArtworksDirectory(AccountInfo account) => GetOrCreateAbsoluteDirectory(GetRelEmbeddedArtworkDirectory(account));
    public string GetOrCreateAbsoluteLyricsDirectory(AccountInfo account) => GetOrCreateAbsoluteDirectory(GetRelLyricsDirectory(account));

    // --- file operations -----------------------------------------------------------------------

    /// Moves a (downloaded) file into the cache at the given relative path, replacing an existing file.
    public void MoveItemIntoCache(string sourceAbsPath, string targetRelPath, AccountInfo accountInfo)
    {
        var target = GetAbsolutePath(targetRelPath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target)) RemoveItem(target, accountInfo);
        File.Move(sourceAbsPath, target);
        UpdateCachedDirectorySize(target, isAdded: true, accountInfo);
    }

    /// Writes data into the cache at the given relative path.
    public void WriteDataIntoCache(byte[] data, string targetRelPath, AccountInfo accountInfo)
    {
        var target = GetAbsolutePath(targetRelPath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target)) RemoveItem(target, accountInfo);
        File.WriteAllBytes(target, data);
        UpdateCachedDirectorySize(target, isAdded: true, accountInfo);
    }

    public string CreateTempFilePath() => Path.Combine(Path.GetTempPath(), "Amperfy", Guid.NewGuid().ToString("N"));

    public void RemoveItem(string absPath, AccountInfo accountInfo)
    {
        UpdateCachedDirectorySize(absPath, isAdded: false, accountInfo);
        if (File.Exists(absPath)) File.Delete(absPath);
        else if (Directory.Exists(absPath)) Directory.Delete(absPath, recursive: true);
    }

    public void RemoveRelItem(string relPath, AccountInfo accountInfo) => RemoveItem(GetAbsolutePath(relPath), accountInfo);

    private void UpdateCachedDirectorySize(string itemPath, bool isAdded, AccountInfo accountInfo)
    {
        var songsDir = GetAbsolutePath(GetRelSongsDirectory(accountInfo));
        var episodesDir = GetAbsolutePath(GetRelPodcastEpisodesDirectory(accountInfo));
        if (!IsInside(itemPath, songsDir) && !IsInside(itemPath, episodesDir)) return;
        var size = GetFileSize(itemPath);
        if (size is null) return;
        lock (_sizeLock)
        {
            var delta = isAdded ? size.Value : -size.Value;
            _completePlayableCacheSize += delta;
            _accountPlayableCacheSize[accountInfo] = _accountPlayableCacheSize.GetValueOrDefault(accountInfo) + delta;
        }
    }

    private static bool IsInside(string filePath, string dirPath)
    {
        var f = Path.GetFullPath(filePath);
        var d = Path.GetFullPath(dirPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return f.StartsWith(d, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    public void DeleteAccountCache(AccountInfo accountInfo)
    {
        TryDeleteDirectory(GetAbsolutePath(GetRelPath(accountInfo)));
        lock (_sizeLock)
        {
            _accountPlayableCacheSize.Remove(accountInfo);
            _completePlayableCacheSize = _accountPlayableCacheSize.Values.Sum();
        }
    }

    public void DeletePlayableCache(AccountInfo accountInfo)
    {
        TryDeleteDirectory(GetAbsolutePath(GetRelSongsDirectory(accountInfo)));
        TryDeleteDirectory(GetAbsolutePath(GetRelPodcastEpisodesDirectory(accountInfo)));
        TryDeleteDirectory(GetAbsolutePath(GetRelEmbeddedArtworkDirectory(accountInfo)));
        lock (_sizeLock)
        {
            _accountPlayableCacheSize[accountInfo] = 0;
            _completePlayableCacheSize = _accountPlayableCacheSize.Values.Sum();
        }
    }

    public void DeleteRemoteArtworkCache(AccountInfo accountInfo) => TryDeleteDirectory(GetAbsolutePath(GetRelArtworkDirectory(accountInfo)));

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception ex) { AmperfyLog.Warning("CacheFileManager", $"Could not delete {path}: {ex.Message}"); }
    }

    public static long DirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        long size = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(f).Length; } catch { }
            }
        }
        catch { }
        return size;
    }

    /// All accounts which have a cache directory.
    public List<AccountInfo> GetAccounts()
    {
        var result = new List<AccountInfo>();
        var accountsDir = GetAbsolutePath(AccountsDir);
        if (!Directory.Exists(accountsDir)) return result;
        foreach (var serverDir in Directory.EnumerateDirectories(accountsDir))
        foreach (var userDir in Directory.EnumerateDirectories(serverDir))
            result.Add(new AccountInfo(Path.GetFileName(serverDir), Path.GetFileName(userDir)));
        return result;
    }

    public sealed record PlayableCacheInfo(string AbsPath, string Id, string FileType, string? MimeType, string RelFilePath);

    public List<PlayableCacheInfo> GetCachedSongs(AccountInfo account) => GetCachedPlayables(GetRelSongsDirectory(account));

    public List<PlayableCacheInfo> GetCachedEpisodes(AccountInfo account) => GetCachedPlayables(GetRelPodcastEpisodesDirectory(account));

    private List<PlayableCacheInfo> GetCachedPlayables(string relDir)
    {
        var result = new List<PlayableCacheInfo>();
        var abs = GetAbsolutePath(relDir);
        if (!Directory.Exists(abs)) return result;
        foreach (var file in Directory.EnumerateFiles(abs))
        {
            var fileName = Path.GetFileName(file);
            var ext = Path.GetExtension(file).TrimStart('.');
            var id = string.IsNullOrEmpty(ext) ? fileName : Path.GetFileNameWithoutExtension(file);
            result.Add(new PlayableCacheInfo(file, id, ext, string.IsNullOrEmpty(ext) ? null : MimeFileConverter.GetMimeType(ext), $"{relDir}/{fileName}"));
        }
        return result;
    }

    public sealed record ArtworkCacheInfo(string AbsPath, string Id, string Type, string RelFilePath);

    public List<ArtworkCacheInfo> GetCachedArtworks(AccountInfo account)
    {
        var result = new List<ArtworkCacheInfo>();
        var relDir = GetRelArtworkDirectory(account);
        var abs = GetAbsolutePath(relDir);
        if (!Directory.Exists(abs)) return result;
        foreach (var file in Directory.EnumerateFiles(abs))
            result.Add(new ArtworkCacheInfo(file, Path.GetFileNameWithoutExtension(file), "", $"{relDir}/{Path.GetFileName(file)}"));
        foreach (var typeDir in Directory.EnumerateDirectories(abs))
        {
            var type = Path.GetFileName(typeDir);
            foreach (var file in Directory.EnumerateFiles(typeDir))
                result.Add(new ArtworkCacheInfo(file, Path.GetFileNameWithoutExtension(file), type, $"{relDir}/{type}/{Path.GetFileName(file)}"));
        }
        return result;
    }

    public sealed record EmbeddedArtworkCacheInfo(string AbsPath, string Id, bool IsSong, string RelFilePath);

    public List<EmbeddedArtworkCacheInfo> GetCachedEmbeddedArtworks(AccountInfo account)
    {
        var result = new List<EmbeddedArtworkCacheInfo>();
        foreach (var isSong in new[] { true, false })
        {
            var relDir = $"{GetRelEmbeddedArtworkDirectory(account)}/{(isSong ? SongsDir : EpisodesDir)}";
            var abs = GetAbsolutePath(relDir);
            if (!Directory.Exists(abs)) continue;
            foreach (var file in Directory.EnumerateFiles(abs))
                result.Add(new EmbeddedArtworkCacheInfo(file, Path.GetFileNameWithoutExtension(file), isSong, $"{relDir}/{Path.GetFileName(file)}"));
        }
        return result;
    }

    public sealed record LyricsCacheInfo(string AbsPath, string Id, string RelFilePath);

    public List<LyricsCacheInfo> GetCachedLyrics(AccountInfo account)
    {
        var result = new List<LyricsCacheInfo>();
        var relDir = $"{GetRelLyricsDirectory(account)}/{SongsDir}";
        var abs = GetAbsolutePath(relDir);
        if (!Directory.Exists(abs)) return result;
        foreach (var file in Directory.EnumerateFiles(abs))
            result.Add(new LyricsCacheInfo(file, Path.GetFileNameWithoutExtension(file), $"{relDir}/{Path.GetFileName(file)}"));
        return result;
    }
}
