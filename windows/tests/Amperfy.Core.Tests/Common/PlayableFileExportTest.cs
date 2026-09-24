using Amperfy.Core.Tests.Helper;

namespace Amperfy.Core.Tests.Common;

public class PlayableFileExportTest
{
    [Theory]
    [InlineData("AC/DC - Back: In Black", "AC-DC - Back- In Black")]
    [InlineData("What? *Now* <live> \"x\" a|b c\\d", "What- -Now- -live- -x- a-b c-d")]
    [InlineData("Title.", "Title")]
    [InlineData("  Title  ", "Title")]
    [InlineData("", "Amperfy")]
    [InlineData("...", "Amperfy")]
    public void SanitizedFileName_ReplacesInvalidCharacters(string name, string expected)
    {
        Assert.Equal(expected, PlayableFileExport.SanitizedFileName(name));
    }

    [Fact]
    public void ExportFileName_UsesDisplayStringAndCachedFileExtension()
    {
        using var storage = new TestStorage();
        var library = storage.Library;
        var artist = library.CreateArtist(storage.Account);
        artist.Name = "Artist/Name";
        var song = library.CreateSong(storage.Account);
        song.Title = "Song: 1";
        song.Artist = artist;
        library.SaveContext();

        Assert.Equal("Artist-Name - Song- 1.flac", PlayableFileExport.ExportFileName(song, Path.Combine("cache", "songs", "123.flac")));
        Assert.Equal("Artist-Name - Song- 1", PlayableFileExport.ExportFileName(song, Path.Combine("cache", "songs", "123")));
    }
}
