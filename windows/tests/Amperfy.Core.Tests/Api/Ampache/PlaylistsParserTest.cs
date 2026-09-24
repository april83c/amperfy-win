using System.Globalization;
using Amperfy.Core.Api.Ampache;
using Amperfy.Core.Tests.Helper;

namespace Amperfy.Core.Tests.Api.Ampache;

/// Port of PlaylistsParserTest.swift
public class PlaylistsParserTest : AbstractAmpacheTest
{
    public PlaylistsParserTest()
    {
        XmlData = GetTestFileData("playlists");
    }

    protected override void CreateParserDelegate()
    {
        ParserDelegate = new PlaylistParserDelegate(Account, Library, parseNotifier: null);
    }

    [Fact]
    public void TestLibraryContainsBeforeMorePlaylistsThenAfter()
    {
        for (var i = 10; i <= 20; i++)
        {
            var playlist = Library.CreatePlaylist(Account);
            playlist.Id = i.ToString(CultureInfo.InvariantCulture);
            playlist.Name = i.ToString(CultureInfo.InvariantCulture);
        }
        Library.SaveContext(); // EF Core queries only see saved entities (CoreData fetches include unsaved ones)
        TestParsing();
    }

    protected override void CheckCorrectParsing()
    {
        var playlists = Library.GetPlaylists(Account);
        Assert.Equal(4, playlists.Count);

        (string Id, string Name, int SongCount)[] expected =
        [
            ("smart_21", "admin - 02/23/2021 14:36:44", 5000),
            ("smart_14", "Album 1*", 2),
            ("3", "random - admin - private", 43),
            ("2", "random - admin - public", 43),
        ];
        for (var i = 0; i < expected.Length; i++)
        {
            var playlist = playlists[i];
            AssertTestAccount1(playlist.Account);
            Assert.Equal(expected[i].Id, playlist.Id);
            Assert.Equal(expected[i].Name, playlist.Name);
            Assert.Equal(expected[i].SongCount, playlist.SongCount);
            Assert.Equal(expected[i].SongCount, playlist.RemoteSongCount);
            Assert.False(playlist.IsCached);
            Assert.Equal(0, playlist.Duration);
            Assert.Equal(0, playlist.RemoteDuration);
        }
    }
}
