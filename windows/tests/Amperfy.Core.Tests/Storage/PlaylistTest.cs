// Ported 1:1 from XCTest: keep the original assertion style (count comparisons, force unwraps).
#pragma warning disable xUnit2013, CS8602

using Amperfy.Core.Player;
using Amperfy.Core.Tests.Helper;
using static Amperfy.Core.Tests.Player.TestUtil;

namespace Amperfy.Core.Tests.Storage;

/// Port of AmperfyKitTests/Cases/Storage/ManagedObjects/PlaylistTest.swift
public class PlaylistTest
{
    private readonly CoreDataHelper cdHelper;
    private readonly LibraryStorage library;
    private readonly Account account;
    private readonly Playlist testPlaylist;
    private readonly Playlist defaultPlaylist;
    private readonly Playlist playlistThreeCached;
    private readonly Playlist playlistNoCached;

    public PlaylistTest()
    {
        cdHelper = new CoreDataHelper();
        library = cdHelper.CreateSeededStorage();
        account = library.GetAccount(TestAccountInfo.Create1());
        defaultPlaylist = NN(library.GetPlaylist(account, cdHelper.Seeder.Playlists[0].Id));
        playlistThreeCached = NN(library.GetPlaylist(account, cdHelper.Seeder.Playlists[1].Id));
        playlistNoCached = NN(library.GetPlaylist(account, cdHelper.Seeder.Playlists[2].Id));
        testPlaylist = library.CreatePlaylist(account);
        ResetTestPlaylist();
    }

    private void ResetTestPlaylist()
    {
        testPlaylist.RemoveAllItems();
        for (var i = 0; i <= 4; i++)
        {
            var song = NN(library.GetSong(account, cdHelper.Seeder.Songs[i].Id));
            testPlaylist.Append(song);
        }
    }

    private void CheckTestPlaylistNoChange()
    {
        for (var i = 0; i <= 4; i++) CheckPlaylistIndexEqualSeedIndex(i, i);
    }

    private void CheckPlaylistIndexEqualSeedIndex(int playlistIndex, int seedIndex)
    {
        var song = NN(library.GetSong(account, cdHelper.Seeder.Songs[seedIndex].Id));
        Assert.Equal(song.Id, testPlaylist.Playables[playlistIndex].Id);
    }


    [Fact]
    public void TestCreation()
    {
        var playlist = library.CreatePlaylist(account);
        Assert.Equal(TestAccountInfo.Test1ServerHash, playlist.Account?.ServerHash);
        Assert.Equal(TestAccountInfo.Test1UserHash, playlist.Account?.UserHash);
        Assert.Equal(0, playlist.Items.Count);
        Assert.Equal("", playlist.Id);
        Assert.Equal(0, playlist.LastPlayableIndex);
        Assert.False(playlist.Playables.HasCachedItems());

        var name = "Test 234";
        playlist.Name = name;
        Assert.Equal(name, playlist.Name);

        var id = "12345";
        playlist.Id = id;
        Assert.Equal(id, playlist.Id);
    }

    [Fact]
    public void TestFetch()
    {
        var playlist = library.CreatePlaylist(account);
        Assert.Equal(TestAccountInfo.Test1ServerHash, playlist.Account?.ServerHash);
        Assert.Equal(TestAccountInfo.Test1UserHash, playlist.Account?.UserHash);
        var id = "12345";
        var name = "Test 234";
        var testAccount = library.GetAccount(TestAccountInfo.Create2());
        playlist.Account = testAccount;
        playlist.Name = name;
        playlist.Id = id;
        library.SaveContext(); // EF queries only see saved data (CoreData fetches include pending changes)
        var playlistFetched = NN(library.GetPlaylist(testAccount, id));
        Assert.Equal(name, playlistFetched.Name);
        Assert.Equal(id, playlistFetched.Id);
        Assert.Equal(TestAccountInfo.Test2ServerHash, playlistFetched.Account?.ServerHash);
        Assert.Equal(TestAccountInfo.Test2UserHash, playlistFetched.Account?.UserHash);
    }

    [Fact]
    public void TestSongAppend()
    {
        var playlist = library.CreatePlaylist(account);
        Assert.Equal(0, playlist.Items.Count);
        var song1 = NN(library.GetSong(account, cdHelper.Seeder.Songs[0].Id));
        playlist.Append(song1);
        Assert.Equal(1, playlist.Items.Count);
        var song2 = NN(library.GetSong(account, cdHelper.Seeder.Songs[1].Id));
        playlist.Append(song2);
        Assert.Equal(2, playlist.Items.Count);
        var song3 = NN(library.GetSong(account, cdHelper.Seeder.Songs[2].Id));
        playlist.Append(song3);
        Assert.Equal(3, playlist.Items.Count);

        foreach (var (index, entry) in playlist.Items.Select((v, i) => (i, v))) {
            Assert.Equal(cdHelper.Seeder.Songs[index].Id, entry.Playable.Id);
            Assert.Equal(TestAccountInfo.Test1ServerHash, entry.Account?.ServerHash);
            Assert.Equal(TestAccountInfo.Test1UserHash, entry.Account?.UserHash);
        }
    }

    [Fact]
    public void TestSongInsertInvalidIndexes()
    {
        var song1 = NN(library.GetSong(account, cdHelper.Seeder.Songs[0].Id));
        var song2 = NN(library.GetSong(account, cdHelper.Seeder.Songs[1].Id));
        ResetTestPlaylist();
        testPlaylist.Insert([song1, song2], -1);
        CheckTestPlaylistNoChange();
        testPlaylist.Insert([song1, song2], -5);
        CheckTestPlaylistNoChange();
        testPlaylist.Insert([], -1);
        CheckTestPlaylistNoChange();
        testPlaylist.Insert([], -5);
        CheckTestPlaylistNoChange();
        testPlaylist.Insert([song1, song2], 6);
        CheckTestPlaylistNoChange();
        testPlaylist.Insert([], 6);
        CheckTestPlaylistNoChange();
        testPlaylist.Insert([song1, song2], 100);
        CheckTestPlaylistNoChange();
        testPlaylist.Insert([], 100);
        CheckTestPlaylistNoChange();
    }

    [Fact]
    public void TestSongInsert()
    {
        var playlist = library.CreatePlaylist(account);
        var song1 = NN(library.GetSong(account, cdHelper.Seeder.Songs[0].Id));
        var song2 = NN(library.GetSong(account, cdHelper.Seeder.Songs[1].Id));
        var song3 = NN(library.GetSong(account, cdHelper.Seeder.Songs[2].Id));
        var song4 = NN(library.GetSong(account, cdHelper.Seeder.Songs[3].Id));
        var song5 = NN(library.GetSong(account, cdHelper.Seeder.Songs[4].Id));
        Assert.Equal(0, playlist.Items.Count);
        playlist.Insert([song5]);
        Assert.Equal(1, playlist.Items.Count);
        playlist.Insert([song4]);
        Assert.Equal(2, playlist.Items.Count);
        playlist.Insert([song1, song2, song3]);
        Assert.Equal(5, playlist.Items.Count);

        foreach (var (index, entry) in playlist.Items.Select((v, i) => (i, v))) {
            if (index > 0) {
                Assert.True(playlist.Items[index - 1].Order < entry.Order);
            }
            Assert.Equal(cdHelper.Seeder.Songs[index].Id, entry.Playable.Id);
            Assert.Equal(TestAccountInfo.Test1ServerHash, entry.Account?.ServerHash);
            Assert.Equal(TestAccountInfo.Test1UserHash, entry.Account?.UserHash);
        }
    }

    [Fact]
    public void TestSongInsert1()
    {
        var playlist = library.CreatePlaylist(account);
        var song1 = NN(library.GetSong(account, cdHelper.Seeder.Songs[0].Id));
        var song2 = NN(library.GetSong(account, cdHelper.Seeder.Songs[1].Id));
        var song3 = NN(library.GetSong(account, cdHelper.Seeder.Songs[2].Id));
        var song4 = NN(library.GetSong(account, cdHelper.Seeder.Songs[3].Id));
        var song5 = NN(library.GetSong(account, cdHelper.Seeder.Songs[4].Id));
        Assert.Equal(0, playlist.Items.Count);
        playlist.Insert([song5], 0);
        playlist.Insert([song4], 0);
        Assert.Equal(2, playlist.Items.Count);
        playlist.Insert([song1, song2, song3], 0);
        Assert.Equal(5, playlist.Items.Count);

        foreach (var (index, entry) in playlist.Items.Select((v, i) => (i, v))) {
            if (index > 0) {
                Assert.True(playlist.Items[index - 1].Order < entry.Order);
            }
            Assert.Equal(cdHelper.Seeder.Songs[index].Id, entry.Playable.Id);
            Assert.Equal(TestAccountInfo.Test1ServerHash, entry.Account?.ServerHash);
            Assert.Equal(TestAccountInfo.Test1UserHash, entry.Account?.UserHash);
        }
    }

    [Fact]
    public void TestSongInsertCustomIndex()
    {
        var playlist = library.CreatePlaylist(account);
        var song1 = NN(library.GetSong(account, cdHelper.Seeder.Songs[0].Id));
        var song2 = NN(library.GetSong(account, cdHelper.Seeder.Songs[1].Id));
        var song3 = NN(library.GetSong(account, cdHelper.Seeder.Songs[2].Id));
        var song4 = NN(library.GetSong(account, cdHelper.Seeder.Songs[3].Id));
        var song5 = NN(library.GetSong(account, cdHelper.Seeder.Songs[4].Id));
        playlist.Insert([song5], 0);
        playlist.Insert([song4], 0);
        playlist.Insert([song2], 0);
        playlist.Insert([song1], 0);
        playlist.Insert([song3], 2);

        foreach (var (index, entry) in playlist.Items.Select((v, i) => (i, v))) {
            if (index > 0) {
                Assert.True(playlist.Items[index - 1].Order < entry.Order);
            }
            Assert.Equal(cdHelper.Seeder.Songs[index].Id, entry.Playable.Id);
            Assert.Equal(TestAccountInfo.Test1ServerHash, entry.Account?.ServerHash);
            Assert.Equal(TestAccountInfo.Test1UserHash, entry.Account?.UserHash);
        }
    }

    [Fact]
    public void TestSongInsertCustomIndex2()
    {
        var playlist = library.CreatePlaylist(account);
        var song1 = NN(library.GetSong(account, cdHelper.Seeder.Songs[0].Id));
        var song2 = NN(library.GetSong(account, cdHelper.Seeder.Songs[1].Id));
        var song3 = NN(library.GetSong(account, cdHelper.Seeder.Songs[2].Id));
        var song4 = NN(library.GetSong(account, cdHelper.Seeder.Songs[3].Id));
        var song5 = NN(library.GetSong(account, cdHelper.Seeder.Songs[4].Id));
        playlist.Insert([song4], 0);
        playlist.Insert([song3], 0);
        playlist.Insert([song2], 0);
        playlist.Insert([song1], 0);
        playlist.Insert([song5], 4);

        foreach (var (index, entry) in playlist.Items.Select((v, i) => (i, v))) {
            if (index > 0) {
                Assert.True(playlist.Items[index - 1].Order < entry.Order);
            }
            Assert.Equal(cdHelper.Seeder.Songs[index].Id, entry.Playable.Id);
            Assert.Equal(TestAccountInfo.Test1ServerHash, entry.Account?.ServerHash);
            Assert.Equal(TestAccountInfo.Test1UserHash, entry.Account?.UserHash);
        }
    }

    [Fact]
    public void TestSongInsertCustomIndex3()
    {
        var playlist = library.CreatePlaylist(account);
        var song1 = NN(library.GetSong(account, cdHelper.Seeder.Songs[0].Id));
        var song2 = NN(library.GetSong(account, cdHelper.Seeder.Songs[1].Id));
        playlist.Insert([song1], 0);
        playlist.Insert([song2], 1);

        foreach (var (index, entry) in playlist.Items.Select((v, i) => (i, v))) {
            if (index > 0) {
                Assert.True(playlist.Items[index - 1].Order < entry.Order);
            }
            Assert.Equal(cdHelper.Seeder.Songs[index].Id, entry.Playable.Id);
            Assert.Equal(TestAccountInfo.Test1ServerHash, entry.Account?.ServerHash);
            Assert.Equal(TestAccountInfo.Test1UserHash, entry.Account?.UserHash);
        }
    }

    [Fact]
    public void TestSongInsertCustomIndex4()
    {
        var playlist = library.CreatePlaylist(account);
        var song1 = NN(library.GetSong(account, cdHelper.Seeder.Songs[0].Id));
        var song2 = NN(library.GetSong(account, cdHelper.Seeder.Songs[1].Id));
        var song3 = NN(library.GetSong(account, cdHelper.Seeder.Songs[2].Id));
        var song4 = NN(library.GetSong(account, cdHelper.Seeder.Songs[3].Id));
        var song5 = NN(library.GetSong(account, cdHelper.Seeder.Songs[4].Id));
        playlist.Insert([song5], 0);
        playlist.Insert([song1], 0);

        playlist.Insert([song2, song3, song4], 1);

        foreach (var (index, entry) in playlist.Items.Select((v, i) => (i, v))) {
            if (index > 0) {
                Assert.True(playlist.Items[index - 1].Order < entry.Order);
            }
            Assert.Equal(cdHelper.Seeder.Songs[index].Id, entry.Playable.Id);
            Assert.Equal(TestAccountInfo.Test1ServerHash, entry.Account?.ServerHash);
            Assert.Equal(TestAccountInfo.Test1UserHash, entry.Account?.UserHash);
        }
    }

    [Fact]
    public void TestSongInsertEmpty()
    {
        var playlist = library.CreatePlaylist(account);
        var song1 = NN(library.GetSong(account, cdHelper.Seeder.Songs[0].Id));
        var song2 = NN(library.GetSong(account, cdHelper.Seeder.Songs[1].Id));
        var song3 = NN(library.GetSong(account, cdHelper.Seeder.Songs[2].Id));
        Assert.Equal(0, playlist.Items.Count);
        playlist.Insert([]);
        Assert.Equal(0, playlist.Items.Count);
        playlist.Insert([song1, song2, song3]);
        Assert.Equal(3, playlist.Items.Count);
        playlist.Insert([]);
        Assert.Equal(3, playlist.Items.Count);

        foreach (var (index, entry) in playlist.Items.Select((v, i) => (i, v))) {
            if (index > 0) {
                Assert.True(playlist.Items[index - 1].Order < entry.Order);
            }
            Assert.Equal(cdHelper.Seeder.Songs[index].Id, entry.Playable.Id);
            Assert.Equal(TestAccountInfo.Test1ServerHash, entry.Account?.ServerHash);
            Assert.Equal(TestAccountInfo.Test1UserHash, entry.Account?.UserHash);
        }
    }

    [Fact]
    public void TestDefaultPlaylist()
    {
        Assert.True(defaultPlaylist.Playables.HasCachedItems());
        Assert.Equal(5, defaultPlaylist.Playables.Count);
        Assert.Equal(5, defaultPlaylist.Items.Count);
        Assert.Equal(4, defaultPlaylist.LastPlayableIndex);
        Assert.Equal(cdHelper.Seeder.Songs[0].Id, defaultPlaylist.Playables[0].Id);
        Assert.Equal(cdHelper.Seeder.Songs[0].Id, defaultPlaylist.Items[0].Playable.Id);
        Assert.Equal(PlaylistItem.OrderDistance * 1, defaultPlaylist.Items[0].Order);
        Assert.Equal(TestAccountInfo.Test1ServerHash, defaultPlaylist.Items[0].Account?.ServerHash);
        Assert.Equal(TestAccountInfo.Test1UserHash, defaultPlaylist.Items[0].Account?.UserHash);
        Assert.Equal(cdHelper.Seeder.Songs[1].Id, defaultPlaylist.Playables[1].Id);
        Assert.Equal(cdHelper.Seeder.Songs[1].Id, defaultPlaylist.Items[1].Playable.Id);
        Assert.Equal(PlaylistItem.OrderDistance * 2, defaultPlaylist.Items[1].Order);
        Assert.Equal(cdHelper.Seeder.Songs[2].Id, defaultPlaylist.Playables[2].Id);
        Assert.Equal(cdHelper.Seeder.Songs[2].Id, defaultPlaylist.Items[2].Playable.Id);
        Assert.Equal(PlaylistItem.OrderDistance * 3, defaultPlaylist.Items[2].Order);
        Assert.Equal(cdHelper.Seeder.Songs[4].Id, defaultPlaylist.Playables[3].Id);
        Assert.Equal(cdHelper.Seeder.Songs[4].Id, defaultPlaylist.Items[3].Playable.Id);
        Assert.Equal(PlaylistItem.OrderDistance * 4, defaultPlaylist.Items[3].Order);
        Assert.Equal(cdHelper.Seeder.Songs[3].Id, defaultPlaylist.Playables[4].Id);
        Assert.Equal(cdHelper.Seeder.Songs[3].Id, defaultPlaylist.Items[4].Playable.Id);
        Assert.Equal(PlaylistItem.OrderDistance * 5, defaultPlaylist.Items[4].Order);
        Assert.Equal(TestAccountInfo.Test1ServerHash, defaultPlaylist.Items[4].Account?.ServerHash);
        Assert.Equal(TestAccountInfo.Test1UserHash, defaultPlaylist.Items[4].Account?.UserHash);
    }

    [Fact]
    public void TestReorderLastToFirst()
    {
        defaultPlaylist.MovePlaylistItem(2, 0);
        Assert.Equal(cdHelper.Seeder.Songs[2].Id, defaultPlaylist.Items[0].Playable.Id);
        Assert.Equal(cdHelper.Seeder.Songs[0].Id, defaultPlaylist.Items[1].Playable.Id);
        Assert.Equal(cdHelper.Seeder.Songs[1].Id, defaultPlaylist.Items[2].Playable.Id);
    }

    [Fact]
    public void TestReorderSecondToLast()
    {
        defaultPlaylist.MovePlaylistItem(1, 2);
        Assert.Equal(cdHelper.Seeder.Songs[0].Id, defaultPlaylist.Items[0].Playable.Id);
        Assert.Equal(cdHelper.Seeder.Songs[2].Id, defaultPlaylist.Items[1].Playable.Id);
        Assert.Equal(cdHelper.Seeder.Songs[1].Id, defaultPlaylist.Items[2].Playable.Id);
    }

    [Fact]
    public void TestReorderNoChange()
    {
        defaultPlaylist.MovePlaylistItem(1, 1);
        Assert.Equal(cdHelper.Seeder.Songs[0].Id, defaultPlaylist.Items[0].Playable.Id);
        Assert.Equal(cdHelper.Seeder.Songs[1].Id, defaultPlaylist.Items[1].Playable.Id);
        Assert.Equal(cdHelper.Seeder.Songs[2].Id, defaultPlaylist.Items[2].Playable.Id);
    }

    [Fact]
    public void TestEntryRemoval()
    {
        defaultPlaylist.Remove(1);
        Assert.Equal(cdHelper.Seeder.Playlists[0].SongIds.Length - 1, defaultPlaylist.Items.Count);
        Assert.Equal(cdHelper.Seeder.Songs[0].Id, defaultPlaylist.Items[0].Playable.Id);
        Assert.Equal(PlaylistItem.OrderDistance * 1, defaultPlaylist.Items[0].Order);
        Assert.Equal(cdHelper.Seeder.Songs[2].Id, defaultPlaylist.Items[1].Playable.Id);
        Assert.Equal(PlaylistItem.OrderDistance * 3, defaultPlaylist.Items[1].Order);
    }

    [Fact]
    public void TestRemoveFirstOccurrenceOfSong_Success()
    {
        var song1 = NN(library.GetSong(account, cdHelper.Seeder.Songs[1].Id));
        defaultPlaylist.Append(song1);
        defaultPlaylist.RemoveFirstOccurrence( song1);
        Assert.Equal(cdHelper.Seeder.Playlists[0].SongIds.Length, defaultPlaylist.Items.Count);
        Assert.Equal(cdHelper.Seeder.Songs[0].Id, defaultPlaylist.Items[0].Playable.Id);
        Assert.Equal(PlaylistItem.OrderDistance * 1, defaultPlaylist.Items[0].Order);
        Assert.Equal(cdHelper.Seeder.Songs[2].Id, defaultPlaylist.Items[1].Playable.Id);
        Assert.Equal(PlaylistItem.OrderDistance * 3, defaultPlaylist.Items[1].Order);
        Assert.Equal(song1.Id, defaultPlaylist.Items[4].Playable.Id);

        defaultPlaylist.RemoveFirstOccurrence( song1);
        Assert.Equal(cdHelper.Seeder.Playlists[0].SongIds.Length - 1, defaultPlaylist.Items.Count);
        Assert.Equal(cdHelper.Seeder.Songs[0].Id, defaultPlaylist.Items[0].Playable.Id);
        Assert.Equal(PlaylistItem.OrderDistance * 1, defaultPlaylist.Items[0].Order);
        Assert.Equal(cdHelper.Seeder.Songs[2].Id, defaultPlaylist.Items[1].Playable.Id);
        Assert.Equal(PlaylistItem.OrderDistance * 3, defaultPlaylist.Items[1].Order);
        Assert.Equal(cdHelper.Seeder.Songs[3].Id, defaultPlaylist.Items[3].Playable.Id);
        Assert.Equal(PlaylistItem.OrderDistance * 5, defaultPlaylist.Items[3].Order);
    }

    [Fact]
    public void TestRemoveFirstOccurrenceOfSong_NoChange()
    {
        var song6 = NN(library.GetSong(account, cdHelper.Seeder.Songs[6].Id));
        defaultPlaylist.RemoveFirstOccurrence( song6);
        TestDefaultPlaylist();
    }

    [Fact]
    public void TestRemovalAll()
    {
        defaultPlaylist.RemoveAllItems();
        Assert.Equal(0, defaultPlaylist.Items.Count);
    }

    [Fact]
    public void TestGetFirstIndex()
    {
        var song0 = NN(library.GetSong(account, cdHelper.Seeder.Songs[1].Id));
        var foundSongIndex0 = NN(defaultPlaylist.GetFirstIndex(song0));
        Assert.Equal(1, foundSongIndex0);
        Assert.Equal(song0.Id, defaultPlaylist.Items[foundSongIndex0].Playable.Id);
        defaultPlaylist.Append(song0);
        var foundSongIndex1 = NN(defaultPlaylist.GetFirstIndex(song0));
        Assert.Equal(1, foundSongIndex1);
        Assert.Equal(song0.Id, defaultPlaylist.Items[foundSongIndex1].Playable.Id);
        defaultPlaylist.RemoveFirstOccurrence( song0);
        var foundSongIndex2 = NN(defaultPlaylist.GetFirstIndex(song0));
        Assert.Equal(4, foundSongIndex2);
        Assert.Equal(song0.Id, defaultPlaylist.Items[foundSongIndex2].Playable.Id);
        defaultPlaylist.RemoveFirstOccurrence( song0);
        Assert.Null(defaultPlaylist.GetFirstIndex(song0));
    }

    [Fact]
    public void TesthasCachedSongs()
    {
        Assert.False(playlistNoCached.Playables.HasCachedItems());
        Assert.True(defaultPlaylist.Playables.HasCachedItems());
        Assert.True(playlistThreeCached.Playables.HasCachedItems());
    }

    [Fact]
    public void TestMovePlaylistSong_InvalidValues()
    {
        ResetTestPlaylist();

        testPlaylist.MovePlaylistItem(0, 5);
        CheckTestPlaylistNoChange();
        testPlaylist.MovePlaylistItem(0, 20);
        CheckTestPlaylistNoChange();
        testPlaylist.MovePlaylistItem(5, 0);
        CheckTestPlaylistNoChange();
        testPlaylist.MovePlaylistItem(20, 0);
        CheckTestPlaylistNoChange();
        testPlaylist.MovePlaylistItem(-1, 2);
        CheckTestPlaylistNoChange();
        testPlaylist.MovePlaylistItem(-9, 1);
        CheckTestPlaylistNoChange();
        testPlaylist.MovePlaylistItem(1, -1);
        CheckTestPlaylistNoChange();
        testPlaylist.MovePlaylistItem(1, -20);
        CheckTestPlaylistNoChange();
        testPlaylist.MovePlaylistItem(1, 1);
        CheckTestPlaylistNoChange();
        testPlaylist.MovePlaylistItem(4, 4);
        CheckTestPlaylistNoChange();
        testPlaylist.MovePlaylistItem(-5, 30);
        CheckTestPlaylistNoChange();
        testPlaylist.MovePlaylistItem(30, -9);
        CheckTestPlaylistNoChange();
    }

    [Fact]
    public void TestMovePlaylistSong()
    {
        ResetTestPlaylist();
        testPlaylist.MovePlaylistItem(1, 4);
        CheckPlaylistIndexEqualSeedIndex(0, 0);
        CheckPlaylistIndexEqualSeedIndex(1, 2);
        CheckPlaylistIndexEqualSeedIndex(2, 3);
        CheckPlaylistIndexEqualSeedIndex(3, 4);
        CheckPlaylistIndexEqualSeedIndex(4, 1);

        ResetTestPlaylist();
        testPlaylist.MovePlaylistItem(2, 3);
        CheckPlaylistIndexEqualSeedIndex(0, 0);
        CheckPlaylistIndexEqualSeedIndex(1, 1);
        CheckPlaylistIndexEqualSeedIndex(2, 3);
        CheckPlaylistIndexEqualSeedIndex(3, 2);
        CheckPlaylistIndexEqualSeedIndex(4, 4);

        ResetTestPlaylist();
        testPlaylist.MovePlaylistItem(2, 4);
        CheckPlaylistIndexEqualSeedIndex(0, 0);
        CheckPlaylistIndexEqualSeedIndex(1, 1);
        CheckPlaylistIndexEqualSeedIndex(2, 3);
        CheckPlaylistIndexEqualSeedIndex(3, 4);
        CheckPlaylistIndexEqualSeedIndex(4, 2);

        ResetTestPlaylist();
        testPlaylist.MovePlaylistItem(0, 3);
        CheckPlaylistIndexEqualSeedIndex(0, 1);
        CheckPlaylistIndexEqualSeedIndex(1, 2);
        CheckPlaylistIndexEqualSeedIndex(2, 3);
        CheckPlaylistIndexEqualSeedIndex(3, 0);
        CheckPlaylistIndexEqualSeedIndex(4, 4);

        ResetTestPlaylist();
        testPlaylist.MovePlaylistItem(4, 2);
        CheckPlaylistIndexEqualSeedIndex(0, 0);
        CheckPlaylistIndexEqualSeedIndex(1, 1);
        CheckPlaylistIndexEqualSeedIndex(2, 4);
        CheckPlaylistIndexEqualSeedIndex(3, 2);
        CheckPlaylistIndexEqualSeedIndex(4, 3);

        ResetTestPlaylist();
        testPlaylist.MovePlaylistItem(4, 1);
        CheckPlaylistIndexEqualSeedIndex(0, 0);
        CheckPlaylistIndexEqualSeedIndex(1, 4);
        CheckPlaylistIndexEqualSeedIndex(2, 1);
        CheckPlaylistIndexEqualSeedIndex(3, 2);
        CheckPlaylistIndexEqualSeedIndex(4, 3);

        ResetTestPlaylist();
        testPlaylist.MovePlaylistItem(3, 1);
        CheckPlaylistIndexEqualSeedIndex(0, 0);
        CheckPlaylistIndexEqualSeedIndex(1, 3);
        CheckPlaylistIndexEqualSeedIndex(2, 1);
        CheckPlaylistIndexEqualSeedIndex(3, 2);
        CheckPlaylistIndexEqualSeedIndex(4, 4);

        ResetTestPlaylist();
        testPlaylist.MovePlaylistItem(4, 2);
        CheckPlaylistIndexEqualSeedIndex(0, 0);
        CheckPlaylistIndexEqualSeedIndex(1, 1);
        CheckPlaylistIndexEqualSeedIndex(2, 4);
        CheckPlaylistIndexEqualSeedIndex(3, 2);
        CheckPlaylistIndexEqualSeedIndex(4, 3);
    }

    [Fact]
    public void TestMovePlaylistSongToStart()
    {
        ResetTestPlaylist();
        testPlaylist.MovePlaylistItem(1, 0);
        CheckPlaylistIndexEqualSeedIndex(0, 1);
        CheckPlaylistIndexEqualSeedIndex(1, 0);
        CheckPlaylistIndexEqualSeedIndex(2, 2);
        CheckPlaylistIndexEqualSeedIndex(3, 3);
        CheckPlaylistIndexEqualSeedIndex(4, 4);

        ResetTestPlaylist();
        testPlaylist.MovePlaylistItem(2, 0);
        CheckPlaylistIndexEqualSeedIndex(0, 2);
        CheckPlaylistIndexEqualSeedIndex(1, 0);
        CheckPlaylistIndexEqualSeedIndex(2, 1);
        CheckPlaylistIndexEqualSeedIndex(3, 3);
        CheckPlaylistIndexEqualSeedIndex(4, 4);

        ResetTestPlaylist();
        testPlaylist.MovePlaylistItem(4, 0);
        CheckPlaylistIndexEqualSeedIndex(0, 4);
        CheckPlaylistIndexEqualSeedIndex(1, 0);
        CheckPlaylistIndexEqualSeedIndex(2, 1);
        CheckPlaylistIndexEqualSeedIndex(3, 2);
        CheckPlaylistIndexEqualSeedIndex(4, 3);
    }

    [Fact]
    public void TestMovePlaylistSongToEnd()
    {
        ResetTestPlaylist();
        testPlaylist.MovePlaylistItem(0, 4);
        CheckPlaylistIndexEqualSeedIndex(0, 1);
        CheckPlaylistIndexEqualSeedIndex(1, 2);
        CheckPlaylistIndexEqualSeedIndex(2, 3);
        CheckPlaylistIndexEqualSeedIndex(3, 4);
        CheckPlaylistIndexEqualSeedIndex(4, 0);

        ResetTestPlaylist();
        testPlaylist.MovePlaylistItem(2, 4);
        CheckPlaylistIndexEqualSeedIndex(0, 0);
        CheckPlaylistIndexEqualSeedIndex(1, 1);
        CheckPlaylistIndexEqualSeedIndex(2, 3);
        CheckPlaylistIndexEqualSeedIndex(3, 4);
        CheckPlaylistIndexEqualSeedIndex(4, 2);
    }
}
