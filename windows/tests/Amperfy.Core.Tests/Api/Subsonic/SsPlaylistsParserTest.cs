using Amperfy.Core.Tests.Helper;
using Amperfy.Core.Api.Subsonic;

namespace Amperfy.Core.Tests.Api.Subsonic;

public class SsPlaylistsParserTest : AbstractSsParserTest
{
    public SsPlaylistsParserTest()
    {
        XmlData = GetTestFileData("playlists_example_1");
    }

    protected override void CreateParserDelegate()
    {
        SsParserDelegate = new SsPlaylistParserDelegate(Account, Library);
    }

    [Fact]
    public void TestLibraryContainsBeforeMorePlaylistsThenAfter()
    {
        for (var i = 20; i <= 30; i++)
        {
            var playlist = Library.CreatePlaylist(Account);
            playlist.Id = i.ToString();
            playlist.Name = i.ToString();
        }
        Library.SaveContext();
        TestParsing();
    }

    protected override void CheckCorrectParsing()
    {
        var playlists = Library.GetPlaylists(Account);
        Assert.Equal(2, playlists.Count);

        var playlist = playlists[1];
        AssertIsAccount1(playlist.Account);
        Assert.Equal("15", playlist.Id);
        Assert.Equal("Some random songs", playlist.Name);
        Assert.Equal(6, playlist.SongCount);
        Assert.Equal(6, playlist.RemoteSongCount);
        Assert.Equal(1391, playlist.Duration);
        Assert.Equal(1391, playlist.RemoteDuration);
        Assert.False(playlist.IsCached);

        playlist = playlists[0];
        AssertIsAccount1(playlist.Account);
        Assert.Equal("16", playlist.Id);
        Assert.Equal("More random songs", playlist.Name);
        Assert.Equal(5, playlist.SongCount);
        Assert.Equal(5, playlist.RemoteSongCount);
        Assert.Equal(1018, playlist.Duration);
        Assert.Equal(1018, playlist.RemoteDuration);
        Assert.False(playlist.IsCached);
    }
}
