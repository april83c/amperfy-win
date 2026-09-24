namespace Amperfy.Core.Downloads;

/// Parts of the Swift CacheFileManager needed by the download code which are not part of the
/// shared <see cref="CacheFileManager"/>.
public static class CacheFileManagerExtensions
{
    /// Swift: CacheFileManager.maxFileSizeToHandleDataInMemory
    public const long MaxFileSizeToHandleDataInMemory = 50_000_000;

    /// Relative cache path of a playable file (Swift: createRelPath(for playable:)). The extension is
    /// derived from the transcoded MIME type (if any) or the original one. Null if no id/account.
    public static string? CreateRelPath(this CacheFileManager _, AbstractPlayable playable)
    {
        if (string.IsNullOrEmpty(playable.Id) || playable.Account is not { } account) return null;
        var mimeType = playable.ContentTypeTranscoded ?? playable.ContentType;
        return playable.IsSong
            ? CacheFileManager.GetRelSongFilePath(account.Info, playable.Id, mimeType)
            : CacheFileManager.GetRelEpisodeFilePath(account.Info, playable.Id, mimeType);
    }

    /// Relative cache path of an embedded artwork (Swift: createRelPath(for embeddedArtwork:)).
    public static string? CreateRelPath(this CacheFileManager _, EmbeddedArtwork embeddedArtwork)
    {
        if (embeddedArtwork.Owner is not { } owner || string.IsNullOrEmpty(owner.Id)) return null;
        var account = embeddedArtwork.Account ?? owner.Account;
        if (account is null) return null;
        return CacheFileManager.GetRelEmbeddedArtworkFilePath(account.Info, owner.Id, owner.IsSong);
    }

    /// File content if the file is smaller than <paramref name="maxFileSize"/> bytes (Swift: getFileDataIfNotToBig).
    public static byte[]? GetFileDataIfNotTooBig(this CacheFileManager fileManager, string? absFilePath, long maxFileSize = MaxFileSizeToHandleDataInMemory)
    {
        if (absFilePath is null) return null;
        var size = fileManager.GetFileSize(absFilePath);
        if (size is null || size.Value >= maxFileSize) return null;
        try { return File.ReadAllBytes(absFilePath); }
        catch { return null; }
    }

    /// Creates a unique temp file path whose directory exists.
    public static string CreateTempFilePathWithDirectory(this CacheFileManager fileManager)
    {
        var path = fileManager.CreateTempFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    public static void TryDeleteFile(string? absPath)
    {
        if (absPath is null) return;
        try { if (File.Exists(absPath)) File.Delete(absPath); }
        catch (Exception ex) { AmperfyLog.Warning("CacheFileManager", $"Could not delete {absPath}: {ex.Message}"); }
    }
}
