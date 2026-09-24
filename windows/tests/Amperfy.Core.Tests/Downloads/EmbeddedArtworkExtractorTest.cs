using Amperfy.Core.Downloads;

namespace Amperfy.Core.Tests.Downloads;

public static class TestMediaFiles
{
    public static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9];
    public static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01];

    /// Writes a tiny MP3 (a few silent MPEG-1 Layer III frames) and adds the pictures as ID3v2 APIC frames.
    public static byte[] CreateMp3(params (TagLib.PictureType Type, string MimeType, byte[] Data)[] pictures)
    {
        var path = Path.Combine(Path.GetTempPath(), "amperfy-tests", $"{Guid.NewGuid():N}.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            // MPEG-1 Layer III, 128 kbps, 44.1 kHz, no padding -> 417 bytes per frame
            const int frameLength = 417;
            var data = new byte[frameLength * 10];
            for (var i = 0; i < 10; i++)
            {
                data[i * frameLength] = 0xFF;
                data[i * frameLength + 1] = 0xFB;
                data[i * frameLength + 2] = 0x90;
                data[i * frameLength + 3] = 0x00;
            }
            File.WriteAllBytes(path, data);
            if (pictures.Length > 0)
            {
                using var file = TagLib.File.Create(path);
                file.Tag.Title = "Test";
                file.Tag.Pictures = pictures.Select(p => (TagLib.IPicture)new TagLib.Picture(new TagLib.ByteVector(p.Data))
                {
                    Type = p.Type,
                    MimeType = p.MimeType,
                    Description = p.Type.ToString(),
                }).ToArray();
                file.Save();
            }
            return File.ReadAllBytes(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

[Collection(DownloadTestCollection.Name)]
public class EmbeddedArtworkExtractorTest : IDisposable
{
    private readonly DownloadTestContext _c = new();

    public void Dispose() => _c.Dispose();

    private Song CreateCachedSong(string id, byte[] fileContent, string extension = "mp3", string? contentType = "audio/mpeg")
    {
        var song = _c.CreateSong(id, contentType);
        var rel = $"{CacheFileManager.GetRelSongsDirectory(_c.Account.Info)}/{id}.{extension}";
        _c.FileManager.WriteDataIntoCache(fileContent, rel, _c.Account.Info);
        song.RelFilePath = rel;
        _c.Library.SaveContext();
        return song;
    }

    [Fact]
    public void ReadPicturesFromId3Tag()
    {
        var mp3 = TestMediaFiles.CreateMp3((TagLib.PictureType.FrontCover, "image/jpeg", TestMediaFiles.JpegBytes));
        var path = Path.Combine(_c.T.CacheDir, "read.mp3");
        File.WriteAllBytes(path, mp3);
        var picture = Assert.Single(EmbeddedArtworkExtractor.ReadPictures(path));
        Assert.True(picture.IsFrontCover);
        Assert.Equal("image/jpeg", picture.MimeType);
        Assert.Equal(TestMediaFiles.JpegBytes, picture.Data);
    }

    [Fact]
    public void ExtractsFrontCoverIntoCache()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var mp3 = TestMediaFiles.CreateMp3(
                (TagLib.PictureType.Other, "image/png", TestMediaFiles.PngBytes),
                (TagLib.PictureType.FrontCover, "image/jpeg", TestMediaFiles.JpegBytes));
            var song = CreateCachedSong("s1", mp3);

            await new EmbeddedArtworkExtractor(_c.FileManager).ExtractEmbeddedArtworkAsync(song, _c.Library);

            var embedded = song.EmbeddedArtwork;
            Assert.NotNull(embedded);
            Assert.Equal(CacheFileManager.GetRelEmbeddedArtworkFilePath(_c.Account.Info, "s1", isSong: true), embedded.RelFilePath);
            Assert.Equal(TestMediaFiles.JpegBytes, File.ReadAllBytes(_c.FileManager.GetAbsolutePath(embedded.RelFilePath!)));
            Assert.Same(song, embedded.Owner);
            Assert.Same(embedded, _c.Library.GetEmbeddedArtwork(song));
            Assert.Equal(embedded.ImagePath, song.ImagePath(ArtworkDisplayPreference.Id3TagOnly));
        });
    }

    [Fact]
    public void FallsBackToFirstValidImage()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var mp3 = TestMediaFiles.CreateMp3(
                (TagLib.PictureType.FrontCover, "image/jpeg", [1, 2, 3, 4, 5]), // not an image
                (TagLib.PictureType.BackCover, "image/png", TestMediaFiles.PngBytes));
            var song = CreateCachedSong("s1", mp3);
            await new EmbeddedArtworkExtractor(_c.FileManager).ExtractEmbeddedArtworkAsync(song, _c.Library);
            Assert.Equal(TestMediaFiles.PngBytes, File.ReadAllBytes(_c.FileManager.GetAbsolutePath(song.EmbeddedArtwork!.RelFilePath!)));
        });
    }

    [Fact]
    public void NoPictureNoEmbeddedArtwork()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var song = CreateCachedSong("s1", TestMediaFiles.CreateMp3());
            var notAudio = CreateCachedSong("s2", [1, 2, 3]);
            var extractor = new EmbeddedArtworkExtractor(_c.FileManager);
            await extractor.ExtractEmbeddedArtworkAsync(song, _c.Library);
            await extractor.ExtractEmbeddedArtworkAsync(notAudio, _c.Library);
            Assert.Null(song.EmbeddedArtwork);
            Assert.Null(notAudio.EmbeddedArtwork);
            Assert.Empty(_c.Library.GetEmbeddedArtworks(_c.Account));
        });
    }

    [Fact]
    public void UnknownFileExtensionUsesMimeType()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var mp3 = TestMediaFiles.CreateMp3((TagLib.PictureType.FrontCover, "image/jpeg", TestMediaFiles.JpegBytes));
            var song = CreateCachedSong("s1", mp3, extension: MimeFileConverter.FilenameExtensionUnknown, contentType: "audio/mpeg");
            await new EmbeddedArtworkExtractor(_c.FileManager).ExtractEmbeddedArtworkAsync(song, _c.Library);
            Assert.NotNull(song.EmbeddedArtwork?.RelFilePath);
        });
    }

    [Fact]
    public void ExistingEmbeddedArtworkIsReused()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var song = CreateCachedSong("s1", TestMediaFiles.CreateMp3((TagLib.PictureType.FrontCover, "image/jpeg", TestMediaFiles.JpegBytes)));
            var extractor = new EmbeddedArtworkExtractor(_c.FileManager);
            await extractor.ExtractEmbeddedArtworkAsync(song, _c.Library);
            var first = song.EmbeddedArtwork;
            await extractor.ExtractEmbeddedArtworkAsync(song, _c.Library);
            Assert.Same(first, song.EmbeddedArtwork);
            Assert.Single(_c.Library.GetEmbeddedArtworks(_c.Account));
        });
    }

    [Fact]
    public void EpisodeArtworkIsStoredInEpisodesDirectory()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var episode = _c.CreateEpisode("e1");
            var rel = CacheFileManager.GetRelEpisodeFilePath(_c.Account.Info, "e1", "audio/mpeg");
            _c.FileManager.WriteDataIntoCache(TestMediaFiles.CreateMp3((TagLib.PictureType.FrontCover, "image/png", TestMediaFiles.PngBytes)), rel, _c.Account.Info);
            episode.RelFilePath = rel;
            _c.Library.SaveContext();

            await new EmbeddedArtworkExtractor(_c.FileManager).ExtractEmbeddedArtworkAsync(episode, _c.Library);
            Assert.Equal(CacheFileManager.GetRelEmbeddedArtworkFilePath(_c.Account.Info, "e1", isSong: false), episode.EmbeddedArtwork?.RelFilePath);
        });
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xDB }, true)]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, true)]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, true)]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, true)]
    [InlineData(new byte[] { 1, 2, 3, 4, 5 }, false)]
    [InlineData(new byte[] { 0xFF }, false)]
    public void ImageDataDetection(byte[] data, bool isImage) => Assert.Equal(isImage, EmbeddedArtworkExtractor.IsImageData(data));
}
