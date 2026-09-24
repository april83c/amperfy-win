// Ported 1:1 from XCTest: keep the original assertion style (count comparisons, force unwraps).
#pragma warning disable xUnit2013, CS8602

using Amperfy.Core.Player;
using Amperfy.Core.Tests.Helper;
using static Amperfy.Core.Tests.Player.TestUtil;

namespace Amperfy.Core.Tests.Storage;

/// Port of AmperfyKitTests/Cases/Storage/ManagedObjects/PlayerDataTest.swift
public class PlayerDataTest
{
    private readonly CoreDataHelper cdHelper;
    private readonly LibraryStorage library;
    private readonly Account account;
    private readonly PlayerData testPlayer;
    private readonly Playlist testNormalPlaylist;
    private readonly Playlist testShuffledPlaylist;
    private readonly Playlist testPodcastPlaylist;
    private const int fillCount = 5;

    public PlayerDataTest()
    {
        cdHelper = new CoreDataHelper();
        library = cdHelper.CreateSeededStorage();
        account = library.GetAccount(TestAccountInfo.Create1());
        testPlayer = library.GetPlayerData();
        testPlayer.SetShuffle(true);
        testShuffledPlaylist = testPlayer.ActiveQueue;
        testPlayer.SetShuffle(false);
        testNormalPlaylist = testPlayer.ActiveQueue;
        testPodcastPlaylist = testPlayer.PodcastQueue;
    }

    private void FillPlayerWithSomeSongs()
    {
        for (var i = 0; i <= fillCount - 1; i++)
        {
            var song = NN(library.GetSong(account, cdHelper.Seeder.Songs[i].Id));
            testPlayer.AppendActiveQueue([song]);
        }
    }

    private void CheckCorrectDefaultPlaylist()
    {
        for (var i = 0; i <= fillCount - 1; i++) CheckPlaylistIndexEqualSeedIndex(i, i);
    }

    private void CheckPlaylistIndexEqualSeedIndex(int playlistIndex, int seedIndex)
    {
        var song = NN(library.GetSong(account, cdHelper.Seeder.Songs[seedIndex].Id));
        Assert.Equal(song.Id, testPlayer.ActiveQueue.Playables[playlistIndex].Id);
    }


    [Fact]
    public void TestCreation()
    {
        Assert.NotEqual(testShuffledPlaylist, testNormalPlaylist);
        Assert.Equal(testNormalPlaylist, testPlayer.ActiveQueue);
        Assert.Null(testPlayer.CurrentItem);
        Assert.False(testPlayer.IsShuffle);
        Assert.Equal(RepeatMode.Off, testPlayer.RepeatMode);
        Assert.Equal(0, testPlayer.CurrentIndex);
        Assert.Equal(0, testNormalPlaylist.Playables.Count);
        Assert.Equal(0, testShuffledPlaylist.Playables.Count);
    }

    [Fact]
    public void TestPlaylist()
    {
        FillPlayerWithSomeSongs();
        Assert.Equal(fillCount, testPlayer.ActiveQueue.Playables.Count);
        CheckCorrectDefaultPlaylist();
    }

    [Fact]
    public void TestCurrentSong()
    {
        FillPlayerWithSomeSongs();

        foreach (var i in new[] { 3, 2, 4, 1, 0 }) {
            var song = NN(library.GetSong(account, cdHelper.Seeder.Songs[i].Id));
            testPlayer.SetCurrentIndex(i);
            Assert.Equal(song.Id, testPlayer.CurrentItem?.Id);
        }
    }

    [Fact]
    public void TestShuffle()
    {
        testPlayer.SetShuffle(true);
        Assert.True(testPlayer.IsShuffle);
        Assert.Equal(testShuffledPlaylist, testPlayer.ActiveQueue);
        testPlayer.SetShuffle(false);
        Assert.False(testPlayer.IsShuffle);
        Assert.Equal(testNormalPlaylist, testPlayer.ActiveQueue);
        testPlayer.SetShuffle(true);
        Assert.Equal(testShuffledPlaylist, testPlayer.ActiveQueue);

        FillPlayerWithSomeSongs();
        Assert.Equal(fillCount, testPlayer.ActiveQueue.Playables.Count);
        testPlayer.SetShuffle(false);
        Assert.Equal(fillCount, testPlayer.ActiveQueue.Playables.Count);
        CheckCorrectDefaultPlaylist();
        testPlayer.SetShuffle(true);
        Assert.Equal(fillCount, testPlayer.ActiveQueue.Playables.Count);
        testPlayer.SetShuffle(false);
        Assert.Equal(fillCount, testPlayer.ActiveQueue.Playables.Count);
        CheckCorrectDefaultPlaylist();
        testPlayer.SetShuffle(true);
    }

    [Fact]
    public void TestRepeat()
    {
        testPlayer.SetRepeatMode(RepeatMode.All);
        Assert.Equal(RepeatMode.All, testPlayer.RepeatMode);
        testPlayer.SetRepeatMode(RepeatMode.Single);
        Assert.Equal(RepeatMode.Single, testPlayer.RepeatMode);
        testPlayer.SetRepeatMode(RepeatMode.Off);
        Assert.Equal(RepeatMode.Off, testPlayer.RepeatMode);
    }

    [Fact]
    public void TestCurrentSongIndexSet()
    {
        FillPlayerWithSomeSongs();
        var curIndex = 2;
        testPlayer.SetCurrentIndex(curIndex);
        Assert.Equal(curIndex, testPlayer.CurrentIndex);
        testPlayer.SetCurrentIndex(-1);
        Assert.Equal(0, testPlayer.CurrentIndex);
        testPlayer.SetCurrentIndex(-2);
        Assert.Equal(0, testPlayer.CurrentIndex);
        testPlayer.SetUserQueuePlaying(true);
        testPlayer.SetCurrentIndex(-1);
        Assert.Equal(-1, testPlayer.CurrentIndex);
        testPlayer.SetCurrentIndex(-2);
        Assert.Equal(-1, testPlayer.CurrentIndex);
        testPlayer.SetUserQueuePlaying(false);
        testPlayer.SetCurrentIndex(-10);
        Assert.Equal(0, testPlayer.CurrentIndex);
        testPlayer.SetCurrentIndex(fillCount - 1);
        Assert.Equal(fillCount - 1, testPlayer.CurrentIndex);
        testPlayer.SetCurrentIndex(fillCount);
        Assert.Equal(0, testPlayer.CurrentIndex);
        testPlayer.SetCurrentIndex(100);
        Assert.Equal(0, testPlayer.CurrentIndex);
    }

    [Fact]
    public void TestAddToPlaylist()
    {
        FillPlayerWithSomeSongs();
        var song1 = NN(library.GetSong(account, cdHelper.Seeder.Songs[6].Id));
        var song2 = NN(library.GetSong(account, cdHelper.Seeder.Songs[7].Id));
        testPlayer.AppendActiveQueue([song1]);
        Assert.Equal(fillCount + 1, testPlayer.ActiveQueue.Playables.Count);
        testPlayer.SetShuffle(true);
        Assert.Equal(fillCount + 1, testPlayer.ActiveQueue.Playables.Count);
        testPlayer.AppendActiveQueue([song2]);
        Assert.Equal(fillCount + 2, testPlayer.ActiveQueue.Playables.Count);
        testPlayer.SetShuffle(false);
        Assert.Equal(fillCount + 2, testPlayer.ActiveQueue.Playables.Count);
        Assert.Equal(song1.Id, testPlayer.ActiveQueue.Playables[fillCount].Id);
        Assert.Equal(song2.Id, testPlayer.ActiveQueue.Playables[fillCount + 1].Id);
    }

    [Fact]
    public void TestRemoveAllSongs()
    {
        FillPlayerWithSomeSongs();
        testPlayer.SetCurrentIndex(3);
        Assert.Equal(3, testPlayer.CurrentIndex);
        testPlayer.RemoveAllItems();
        Assert.Equal(0, testPlayer.CurrentIndex);
        Assert.Equal(0, testPlayer.ActiveQueue.Playables.Count);

        var song1 = NN(library.GetSong(account, cdHelper.Seeder.Songs[6].Id));
        var song2 = NN(library.GetSong(account, cdHelper.Seeder.Songs[7].Id));
        testPlayer.AppendActiveQueue([song1]);
        testPlayer.AppendActiveQueue([song2]);
        testPlayer.RemoveAllItems();
        Assert.Equal(0, testPlayer.ActiveQueue.Playables.Count);
    }
}
