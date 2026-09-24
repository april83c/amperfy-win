namespace Amperfy.Core.Downloads;

/// Picture embedded in a media file tag.
public sealed record EmbeddedPicture(bool IsFrontCover, string? MimeType, byte[] Data);

/// Extracts the cover art embedded in a cached file (ID3v2 / MP4 / FLAC / Ogg / ... via TagLibSharp)
/// and stores it as <see cref="EmbeddedArtwork"/> (port of Player/EmbeddedArtworkExtractor.swift).
///
/// Swift re-encoded the image as PNG; here the original bytes (JPEG/PNG/...) are written as-is to the
/// ".png" path - Windows image decoders detect the format from the content.
public sealed class EmbeddedArtworkExtractor
{
    private readonly CacheFileManager? _fileManager;

    private CacheFileManager FileManager => _fileManager ?? CacheFileManager.Shared;

    public EmbeddedArtworkExtractor(CacheFileManager? fileManager = null)
    {
        _fileManager = fileManager;
    }

    public async Task ExtractEmbeddedArtworkAsync(AbstractPlayable playable, LibraryStorage library)
    {
        if (playable.RelFilePath is not { } relFilePath) return;
        var absFilePath = FileManager.GetAbsolutePath(relFilePath);
        var mimeType = playable.ContentTypeTranscoded ?? playable.ContentType;

        // file parsing does not touch entities -> thread pool
        var pictures = await Task.Run(() => ReadPictures(absFilePath, mimeType));
        if (pictures.Count == 0) return;

        // if there is a front cover artwork take this as embedded artwork, else the first other valid image
        var artwork = pictures.FirstOrDefault(p => p.IsFrontCover && IsImageData(p.Data))
            ?? pictures.FirstOrDefault(p => IsImageData(p.Data));
        if (artwork is null) return;
        SaveEmbeddedImageInLibrary(library, playable, artwork.Data);
    }

    /// Reads all pictures of the file's tags. Returns an empty list if the file can't be parsed.
    public static IReadOnlyList<EmbeddedPicture> ReadPictures(string absFilePath, string? mimeType = null)
    {
        if (!File.Exists(absFilePath)) return [];
        try
        {
            using var file = OpenTagFile(absFilePath, mimeType);
            if (file is null) return [];
            return file.Tag.Pictures
                .Where(p => p?.Data is { Count: > 0 })
                .Select(p => new EmbeddedPicture(p.Type == TagLib.PictureType.FrontCover, p.MimeType, p.Data.Data))
                .ToList();
        }
        catch (Exception ex)
        {
            AmperfyLog.Info("EmbeddedArtworkExtractor", $"Could not read tags of {absFilePath}: {ex.Message}");
            return [];
        }
    }

    private static TagLib.File? OpenTagFile(string absFilePath, string? mimeType)
    {
        try
        {
            return TagLib.File.Create(absFilePath, TagLib.ReadStyle.None);
        }
        catch (TagLib.UnsupportedFormatException)
        {
            // unknown file extension -> try the MIME type of the playable
            if (string.IsNullOrEmpty(mimeType)) return null;
            try
            {
                return TagLib.File.Create(new TagLib.File.LocalFileAbstraction(absFilePath), mimeType.Split(';')[0].Trim(), TagLib.ReadStyle.None);
            }
            catch (TagLib.UnsupportedFormatException)
            {
                return null;
            }
        }
    }

    /// Replacement for Swift `UIImage(data:) != nil`: checks the magic bytes of common image formats.
    public static bool IsImageData(byte[] data)
    {
        if (data.Length < 4) return false;
        // JPEG
        if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return true;
        // PNG
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return true;
        // GIF
        if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38) return true;
        // BMP
        if (data[0] == 0x42 && data[1] == 0x4D) return true;
        // WEBP (RIFF....WEBP)
        if (data.Length >= 12 && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 &&
            data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50) return true;
        // TIFF
        if ((data[0] == 0x49 && data[1] == 0x49 && data[2] == 0x2A && data[3] == 0x00) ||
            (data[0] == 0x4D && data[1] == 0x4D && data[2] == 0x00 && data[3] == 0x2A)) return true;
        return false;
    }

    private void SaveEmbeddedImageInLibrary(LibraryStorage library, AbstractPlayable playable, byte[] imageData)
    {
        if (playable.Account is not { } account) return;
        // reuse an existing embedded artwork (the relation is 1:1)
        var embeddedArtwork = playable.EmbeddedArtwork;
        var isNew = embeddedArtwork is null;
        embeddedArtwork ??= library.CreateEmbeddedArtwork(account);
        embeddedArtwork.Owner = playable;
        playable.EmbeddedArtwork = embeddedArtwork;

        var relFilePath = FileManager.CreateRelPath(embeddedArtwork);
        if (relFilePath is null)
        {
            if (isNew) library.DeleteEmbeddedArtwork(embeddedArtwork);
            return;
        }
        try
        {
            FileManager.WriteDataIntoCache(imageData, relFilePath, account.Info);
            embeddedArtwork.RelFilePath = relFilePath;
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning("EmbeddedArtworkExtractor", $"Could not write embedded artwork: {ex.Message}");
            embeddedArtwork.RelFilePath = null;
        }
        library.SaveContext();
    }
}
