// Ported 1:1 from XCTest: keep the original assertion style (count comparisons, force unwraps).
#pragma warning disable xUnit2013, CS8602

using Amperfy.Core.Player;
using Amperfy.Core.Tests.Helper;
using static Amperfy.Core.Tests.Player.TestUtil;

namespace Amperfy.Core.Tests.Player;

/// Port of AmperfyKitTests/Cases/Player/PlayQueueHandlerTest.swift
public class PlayQueueHandlerTest
{
    private readonly CoreDataHelper cdHelper;
    private readonly LibraryStorage library;
    private readonly Account account;
    private readonly PlayQueueHandler testQueueHandler;
    private readonly PlayerData testPlayer;
    private readonly Playlist testNormalPlaylist;
    private readonly Playlist testShuffledPlaylist;
    private const int fillCount = 5;

    public PlayQueueHandlerTest()
    {
        cdHelper = new CoreDataHelper();
        library = cdHelper.CreateSeededStorage();
        account = library.GetAccount(TestAccountInfo.Create1());
        testPlayer = library.GetPlayerData();
        testPlayer.SetShuffle(true);
        testShuffledPlaylist = testPlayer.ContextQueue;
        testPlayer.SetShuffle(false);
        testNormalPlaylist = testPlayer.ContextQueue;
        testQueueHandler = new PlayQueueHandler(testPlayer);
    }

    private void PrepareNoWaitingQueuePlaying()
    {
        testPlayer.RemoveAllItems();
        FillPlayerWithSomeSongsAndWaitingQueue();
        testPlayer.SetUserQueuePlaying(false);
    }

    private void PrepareWithWaitingQueuePlaying()
    {
        PrepareNoWaitingQueuePlaying();
        testPlayer.SetUserQueuePlaying(true);
    }

    private void FillPlayerWithSomeSongs()
    {
        for (var i = 0; i <= fillCount - 1; i++)
        {
            var song = NN(library.GetSong(account, cdHelper.Seeder.Songs[i].Id));
            testPlayer.AppendContextQueue([song]);
        }
    }

    private void FillPlayerWithSomeSongsAndWaitingQueue()
    {
        FillPlayerWithSomeSongs();
        for (var i = 0; i <= 3; i++)
        {
            var song = NN(library.GetSong(account, cdHelper.Seeder.Songs[fillCount + i].Id));
            testPlayer.AppendUserQueue([song]);
        }
    }

    private void CheckPlaylistIndexEqualSeedIndex(int playlistIndex, int seedIndex)
    {
        var song = NN(library.GetSong(account, cdHelper.Seeder.Songs[seedIndex].Id));
        Assert.Equal(song.Id, testPlayer.ContextQueue.Playables[playlistIndex].Id);
    }

    private void CheckCorrectDefaultPlaylist()
    {
        for (var i = 0; i <= fillCount - 1; i++) CheckPlaylistIndexEqualSeedIndex(i, i);
    }

    private void CheckQueueItems(IReadOnlyList<AbstractPlayable> queue, int[] seedIds)
    {
        Assert.Equal(seedIds.Length, queue.Count);
        if (queue.Count == seedIds.Length && queue.Count > 0)
        {
            for (var i = 0; i <= queue.Count - 1; i++)
            {
                var song = NN(library.GetSong(account, cdHelper.Seeder.Songs[seedIds[i]].Id));
                Assert.Equal(song.Id, queue[i].Id);
            }
        }
    }

    private void CheckCurrentlyPlaying(int? idToBe)
    {
        if (idToBe is { } id)
        {
            var song = NN(library.GetSong(account, cdHelper.Seeder.Songs[id].Id));
            Assert.Equal(song.Id, testQueueHandler.CurrentlyPlaying?.Id);
        }
        else
        {
            Assert.Null(testQueueHandler.CurrentlyPlaying);
        }
    }

    private void CheckQueueInfoConsistency() => TestUtil.CheckQueueInfoConsistency(testQueueHandler);

    private AbstractPlayable song9 => library.GetSong(account, cdHelper.Seeder.Songs[9].Id)!;
    private AbstractPlayable songA => library.GetSong(account, cdHelper.Seeder.Songs[10].Id)!;
    private AbstractPlayable songB => library.GetSong(account, cdHelper.Seeder.Songs[11].Id)!;
    private AbstractPlayable songC => library.GetSong(account, cdHelper.Seeder.Songs[12].Id)!;
    private AbstractPlayable songD => library.GetSong(account, cdHelper.Seeder.Songs[13].Id)!;
    private AbstractPlayable songE => library.GetSong(account, cdHelper.Seeder.Songs[14].Id)!;
    private AbstractPlayable songF => library.GetSong(account, cdHelper.Seeder.Songs[15].Id)!;

    [Fact]
    public void TestCreation() => Run(async () =>
    {
        Assert.Equal(0, testQueueHandler.PrevQueueCount);
        Assert.Empty(testQueueHandler.GetAllPrevQueueItems());
        Assert.Empty(testQueueHandler.GetPrevQueueItems(0, null));
        Assert.Null(testQueueHandler.GetPrevQueueItem(0));
        Assert.Equal(0, testQueueHandler.NextQueueCount);
        Assert.Empty(testQueueHandler.GetAllNextQueueItems());
        Assert.Empty(testQueueHandler.GetNextQueueItems(0, null));
        Assert.Null(testQueueHandler.GetNextQueueItem(0));
        Assert.Equal(0, testQueueHandler.UserQueueCount);
        Assert.Empty(testQueueHandler.GetAllUserQueueItems());
        Assert.Empty(testQueueHandler.GetUserQueueItems(0, null));
        Assert.Null(testQueueHandler.GetUserQueueItem(0));
        Assert.Null(testQueueHandler.CurrentlyPlaying);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestAddToWaitingQueueToEmptyPlayerStartsPlaying() => Run(async () =>
    {
        var song = NN(library.GetSong(account, cdHelper.Seeder.Songs[5].Id));
        testQueueHandler.InsertUserQueue([song]);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        await Task.CompletedTask;
    });

    [Fact]
    public void TestRemoveSongFromPlaylist() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(1);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Next, 0));
        Assert.Equal(fillCount - 1, testPlayer.ContextQueue.Playables.Count);
        Assert.Equal(1, testPlayer.CurrentIndex);
        CheckPlaylistIndexEqualSeedIndex(1, 1);
        CheckPlaylistIndexEqualSeedIndex(2, 3);
        CheckPlaylistIndexEqualSeedIndex(3, 4);

        testPlayer.SetCurrentIndex(3);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Prev, 0));
        Assert.Equal(fillCount - 2, testPlayer.ContextQueue.Playables.Count);
        Assert.Equal(2, testPlayer.CurrentIndex);
        CheckPlaylistIndexEqualSeedIndex(0, 1);
        CheckPlaylistIndexEqualSeedIndex(1, 3);
        CheckPlaylistIndexEqualSeedIndex(2, 4);

        testPlayer.SetCurrentIndex(2);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Prev, 1));
        Assert.Equal(fillCount - 3, testPlayer.ContextQueue.Playables.Count);
        Assert.Equal(1, testPlayer.CurrentIndex);
        CheckPlaylistIndexEqualSeedIndex(0, 1);
        CheckPlaylistIndexEqualSeedIndex(1, 4);

        testPlayer.RemoveAllItems();
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Next, 10));
        Assert.Equal(0, testPlayer.ContextQueue.Playables.Count);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Prev, 10));
        Assert.Equal(0, testPlayer.ContextQueue.Playables.Count);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestQueue_accessInputValidcation() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);

        var prevQueueCount = testQueueHandler.PrevQueueCount;
        Assert.Equal(2, prevQueueCount);
        // end is not valid -> empty set
        var items = testQueueHandler.GetPrevQueueItems(0, prevQueueCount);
        Assert.Equal(0, items.Count);
        // end is not valid -> empty set
        items = testQueueHandler.GetPrevQueueItems(0, -1);
        Assert.Equal(0, items.Count);
        // from is not valid -> empty set
        items = testQueueHandler.GetPrevQueueItems(-1, null);
        Assert.Equal(0, items.Count);
        // from is not valid -> empty set
        items = testQueueHandler.GetPrevQueueItems(5, prevQueueCount - 1);
        Assert.Equal(0, items.Count);
        // from and to is not valid -> empty set
        items = testQueueHandler.GetPrevQueueItems(1, 0);
        Assert.Equal(0, items.Count);

        var userQueueCount = testQueueHandler.UserQueueCount;
        Assert.Equal(4, userQueueCount);
        // end is not valid -> empty set
        items = testQueueHandler.GetUserQueueItems(0, userQueueCount);
        Assert.Equal(0, items.Count);
        // end is not valid -> empty set
        items = testQueueHandler.GetUserQueueItems(0, -1);
        Assert.Equal(0, items.Count);
        // from is not valid -> empty set
        items = testQueueHandler.GetUserQueueItems(-1, null);
        Assert.Equal(0, items.Count);
        // from is not valid -> empty set
        items = testQueueHandler.GetUserQueueItems(5, userQueueCount - 1);
        Assert.Equal(0, items.Count);
        // from and to is not valid -> empty set
        items = testQueueHandler.GetUserQueueItems(1, 0);
        Assert.Equal(0, items.Count);

        var nextQueueCount = testQueueHandler.NextQueueCount;
        Assert.Equal(2, nextQueueCount);
        // end is not valid -> empty set
        items = testQueueHandler.GetNextQueueItems(0, nextQueueCount);
        Assert.Equal(0, items.Count);
        // end is not valid -> empty set
        items = testQueueHandler.GetNextQueueItems(0, -1);
        Assert.Equal(0, items.Count);
        // from is not valid -> empty set
        items = testQueueHandler.GetNextQueueItems(-1, null);
        Assert.Equal(0, items.Count);
        // from is not valid -> empty set
        items = testQueueHandler.GetNextQueueItems(5, nextQueueCount - 1);
        Assert.Equal(0, items.Count);
        // from and to is not valid -> empty set
        items = testQueueHandler.GetNextQueueItems(1, 0);
        Assert.Equal(0, items.Count);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestWaitingQueueInsertFirst_noWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        var song = NN(library.GetSong(account, cdHelper.Seeder.Songs[1].Id));
        testPlayer.SetCurrentIndex(2);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();
        testPlayer.InsertUserQueue([song]);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [1, 5, 6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestWaitingQueueInsertFirst_noWaitingQueuePlaying2() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        var song = NN(library.GetSong(account, cdHelper.Seeder.Songs[1].Id));
        testPlayer.SetCurrentIndex(2);
        CheckCurrentlyPlaying(2);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), []);
        CheckQueueInfoConsistency();
        testPlayer.InsertUserQueue([song]);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [1]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestWaitingQueueInsertFirst_withWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        var song = NN(library.GetSong(account, cdHelper.Seeder.Songs[1].Id));
        testPlayer.SetCurrentIndex(2);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        testPlayer.InsertUserQueue([song]);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [1, 6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestWaitingQueueInsertFirst_withWaitingQueuePlaying2() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        var song = NN(library.GetSong(account, cdHelper.Seeder.Songs[1].Id));
        testPlayer.SetCurrentIndex(2);
        CheckCurrentlyPlaying(5);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), []);
        CheckQueueInfoConsistency();
        testPlayer.InsertUserQueue([song]);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [1]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestWaitingQueueInsertLast_noWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        var song = NN(library.GetSong(account, cdHelper.Seeder.Songs[1].Id));
        testPlayer.SetCurrentIndex(2);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();
        testPlayer.AppendUserQueue([song]);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8, 1]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestWaitingQueueInsertLast_withWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        var song = NN(library.GetSong(account, cdHelper.Seeder.Songs[1].Id));
        testPlayer.SetCurrentIndex(2);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        testPlayer.AppendUserQueue([song]);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8, 1]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestMovePlaylistSong_InvalidValues() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 0), new PlayerIndex(PlayerQueueType.Prev, 5));
        CheckCorrectDefaultPlaylist();
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 0), new PlayerIndex(PlayerQueueType.Prev, 20));
        CheckCorrectDefaultPlaylist();
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 5), new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCorrectDefaultPlaylist();
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 20), new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCorrectDefaultPlaylist();
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 1), new PlayerIndex(PlayerQueueType.Next, 5));
        CheckCorrectDefaultPlaylist();
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 1), new PlayerIndex(PlayerQueueType.Prev, 1));
        CheckCorrectDefaultPlaylist();
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 1), new PlayerIndex(PlayerQueueType.Prev, -1));
        CheckCorrectDefaultPlaylist();
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 1), new PlayerIndex(PlayerQueueType.Prev, -20));
        CheckCorrectDefaultPlaylist();
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 1), new PlayerIndex(PlayerQueueType.Next, 1));
        CheckCorrectDefaultPlaylist();
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 4), new PlayerIndex(PlayerQueueType.Prev, 4));
        CheckCorrectDefaultPlaylist();
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, -1), new PlayerIndex(PlayerQueueType.Next, 30));
        CheckCorrectDefaultPlaylist();
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 30), new PlayerIndex(PlayerQueueType.Prev, -9));
        CheckCorrectDefaultPlaylist();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestMovePlaylistSong() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 1), new PlayerIndex(PlayerQueueType.Next, 1));
        Assert.Equal(1, testPlayer.CurrentIndex);
        CheckPlaylistIndexEqualSeedIndex(0, 0);
        CheckPlaylistIndexEqualSeedIndex(1, 2);
        CheckPlaylistIndexEqualSeedIndex(2, 3);
        CheckPlaylistIndexEqualSeedIndex(3, 1);
        CheckPlaylistIndexEqualSeedIndex(4, 4);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(1);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 0), new PlayerIndex(PlayerQueueType.Next, 1));
        Assert.Equal(1, testPlayer.CurrentIndex);
        CheckPlaylistIndexEqualSeedIndex(0, 0);
        CheckPlaylistIndexEqualSeedIndex(1, 1);
        CheckPlaylistIndexEqualSeedIndex(2, 3);
        CheckPlaylistIndexEqualSeedIndex(3, 2);
        CheckPlaylistIndexEqualSeedIndex(4, 4);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(0);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 3), new PlayerIndex(PlayerQueueType.Next, 4));
        Assert.Equal(0, testPlayer.CurrentIndex);
        CheckPlaylistIndexEqualSeedIndex(0, 0);
        CheckPlaylistIndexEqualSeedIndex(1, 1);
        CheckPlaylistIndexEqualSeedIndex(2, 2);
        CheckPlaylistIndexEqualSeedIndex(3, 3);
        CheckPlaylistIndexEqualSeedIndex(4, 4);
        CheckQueueInfoConsistency();
        testPlayer.RemoveAllItems();
        FillPlayerWithSomeSongs();

        testPlayer.SetCurrentIndex(0);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 2), new PlayerIndex(PlayerQueueType.Next, 3));
        Assert.Equal(0, testPlayer.CurrentIndex);
        CheckPlaylistIndexEqualSeedIndex(0, 0);
        CheckPlaylistIndexEqualSeedIndex(1, 1);
        CheckPlaylistIndexEqualSeedIndex(2, 2);
        CheckPlaylistIndexEqualSeedIndex(3, 4);
        CheckPlaylistIndexEqualSeedIndex(4, 3);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 0), new PlayerIndex(PlayerQueueType.Next, 1));
        Assert.Equal(1, testPlayer.CurrentIndex);
        CheckPlaylistIndexEqualSeedIndex(0, 1);
        CheckPlaylistIndexEqualSeedIndex(1, 2);
        CheckPlaylistIndexEqualSeedIndex(2, 3);
        CheckPlaylistIndexEqualSeedIndex(3, 0);
        CheckPlaylistIndexEqualSeedIndex(4, 4);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 0), new PlayerIndex(PlayerQueueType.Prev, 2));
        Assert.Equal(4, testPlayer.CurrentIndex);
        CheckPlaylistIndexEqualSeedIndex(0, 0);
        CheckPlaylistIndexEqualSeedIndex(1, 1);
        CheckPlaylistIndexEqualSeedIndex(2, 4);
        CheckPlaylistIndexEqualSeedIndex(3, 2);
        CheckPlaylistIndexEqualSeedIndex(4, 3);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 0), new PlayerIndex(PlayerQueueType.Prev, 1));
        CheckPlaylistIndexEqualSeedIndex(0, 0);
        CheckPlaylistIndexEqualSeedIndex(1, 4);
        CheckPlaylistIndexEqualSeedIndex(2, 1);
        CheckPlaylistIndexEqualSeedIndex(3, 2);
        CheckPlaylistIndexEqualSeedIndex(4, 3);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(4);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 2), new PlayerIndex(PlayerQueueType.Next, 0));
        Assert.Equal(3, testPlayer.CurrentIndex);
        CheckPlaylistIndexEqualSeedIndex(0, 0);
        CheckPlaylistIndexEqualSeedIndex(1, 1);
        CheckPlaylistIndexEqualSeedIndex(2, 3);
        CheckPlaylistIndexEqualSeedIndex(3, 4);
        CheckPlaylistIndexEqualSeedIndex(4, 2);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(0);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 3), new PlayerIndex(PlayerQueueType.Prev, 0));
        Assert.Equal(1, testPlayer.CurrentIndex);
        CheckPlaylistIndexEqualSeedIndex(0, 4);
        CheckPlaylistIndexEqualSeedIndex(1, 0);
        CheckPlaylistIndexEqualSeedIndex(2, 1);
        CheckPlaylistIndexEqualSeedIndex(3, 2);
        CheckPlaylistIndexEqualSeedIndex(4, 3);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestQueueCreation_noWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(0);
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        testPlayer.SetCurrentIndex(2);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        testPlayer.SetCurrentIndex(3);
        CheckCurrentlyPlaying(3);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        testPlayer.SetCurrentIndex(4);
        CheckCurrentlyPlaying(4);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestQueueCreation_withWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(-1);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [0, 1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        testPlayer.SetCurrentIndex(0);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        testPlayer.SetCurrentIndex(2);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        testPlayer.SetCurrentIndex(3);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        testPlayer.SetCurrentIndex(4);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestRemovePlayable_noWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Prev, 1));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Next, 1));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 1));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 3));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestRemovePlayable_noWaitingQueuePlaying_edgeCases() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(1);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(1);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(0);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(4);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Prev, 3));
        CheckCurrentlyPlaying(4);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(3);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestRemovePlayable_withWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Prev, 1));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Prev, 2));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Next, 1));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 1));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 2));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestRemovePlayable_withWaitingQueuePlaying_edgeCases() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(0);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(4);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Prev, 4));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 2));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Prev, 2));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Prev, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Prev, 1));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Prev, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Next, 1));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.Next, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 2));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestMove_PrevPrev_noWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 0), new PlayerIndex(PlayerQueueType.Prev, 1));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [1, 0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 1), new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [1, 0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 0), new PlayerIndex(PlayerQueueType.Prev, 2));
        CheckCurrentlyPlaying(3);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [1, 2, 0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 2), new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(3);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [2, 0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(4);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 3), new PlayerIndex(PlayerQueueType.Prev, 2));
        CheckCurrentlyPlaying(4);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 3, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(4);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 3), new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(4);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [3, 0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestMove_PrevPrev_withWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(1);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 0), new PlayerIndex(PlayerQueueType.Prev, 1));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [1, 0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 1), new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [1, 0, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 0), new PlayerIndex(PlayerQueueType.Prev, 3));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [1, 2, 3, 0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 3), new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [3, 0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(4);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 3), new PlayerIndex(PlayerQueueType.Prev, 2));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 3, 2, 4]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(4);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 4), new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [4, 0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestMove_NextNext_noWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 0), new PlayerIndex(PlayerQueueType.Next, 1));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4, 3]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 1), new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4, 3]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(1);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 0), new PlayerIndex(PlayerQueueType.Next, 2));
        CheckCurrentlyPlaying(1);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4, 2]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(1);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 2), new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(1);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(0);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 3), new PlayerIndex(PlayerQueueType.Next, 2));
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 4, 3]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(0);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 3), new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestMove_NextNext_withWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 0), new PlayerIndex(PlayerQueueType.Next, 1));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4, 3]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 1), new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4, 3]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(1);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 0), new PlayerIndex(PlayerQueueType.Next, 2));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4, 2]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(1);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 2), new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(0);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 3), new PlayerIndex(PlayerQueueType.Next, 2));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 4, 3]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(-1);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 4), new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4, 0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(-1);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 0), new PlayerIndex(PlayerQueueType.Next, 3));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 0, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestMove_WaitWait_noWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 0), new PlayerIndex(PlayerQueueType.User, 1));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 5, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 1), new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 5, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(1);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 0), new PlayerIndex(PlayerQueueType.User, 2));
        CheckCurrentlyPlaying(1);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 5, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(1);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 2), new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(1);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [7, 5, 6, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(0);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 3), new PlayerIndex(PlayerQueueType.User, 2));
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 8, 7]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(0);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 3), new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [8, 5, 6, 7]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(4);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 3), new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(4);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [8, 5, 6, 7]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestMove_WaitWait_withWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 0), new PlayerIndex(PlayerQueueType.User, 1));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [7, 6, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 1), new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [7, 6, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(1);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 0), new PlayerIndex(PlayerQueueType.User, 2));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [7, 8, 6]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(1);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 2), new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [8, 6, 7]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(0);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 1), new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [7, 6, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(-1);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 0), new PlayerIndex(PlayerQueueType.User, 1));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [0, 1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [7, 6, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestMove_PrevNext_noWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 0), new PlayerIndex(PlayerQueueType.Next, 1));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 0, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 1), new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(1);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 0), new PlayerIndex(PlayerQueueType.Next, 2));
        CheckCurrentlyPlaying(1);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 3, 0, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 1), new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(3);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 1), new PlayerIndex(PlayerQueueType.Next, 1));
        CheckCurrentlyPlaying(3);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4, 1]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(4);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 0), new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(4);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestMove_PrevNext_withWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 0), new PlayerIndex(PlayerQueueType.Next, 1));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 0, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 1), new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(1);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 1), new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 1), new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 3), new PlayerIndex(PlayerQueueType.Next, 1));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4, 3]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(4);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 0), new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestMove_NextPrev_noWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 0), new PlayerIndex(PlayerQueueType.Prev, 1));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 3, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 1), new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [4, 0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 0), new PlayerIndex(PlayerQueueType.Prev, 2));
        CheckCurrentlyPlaying(3);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 4, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(1);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 1), new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(1);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [3, 0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(1);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 1), new PlayerIndex(PlayerQueueType.Prev, 1));
        CheckCurrentlyPlaying(1);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(0);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 0), new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(0);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 3), new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestMove_NextPrev_withWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 0), new PlayerIndex(PlayerQueueType.Prev, 1));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 3, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 1), new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [4, 0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(1);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 1), new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [3, 0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 0), new PlayerIndex(PlayerQueueType.Prev, 4));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 0), new PlayerIndex(PlayerQueueType.Prev, 3));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 4, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(0);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 3), new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [4, 0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(0);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 3), new PlayerIndex(PlayerQueueType.Prev, 1));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 4]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(-1);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 0), new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(-1);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 4), new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestMove_WaitNext_noWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 0), new PlayerIndex(PlayerQueueType.Next, 1));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 5, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 1), new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [6, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 3), new PlayerIndex(PlayerQueueType.Next, 1));
        CheckCurrentlyPlaying(3);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4, 8]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(4);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 1), new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(4);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [6]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 0), new PlayerIndex(PlayerQueueType.Next, 1));
        CheckCurrentlyPlaying(3);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4, 8]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 1), new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(3);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [8, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [7]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(0);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 2), new PlayerIndex(PlayerQueueType.Next, 4));
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4, 7]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(0);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 2), new PlayerIndex(PlayerQueueType.Next, 3));
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 7, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestMove_WaitNext_withWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 0), new PlayerIndex(PlayerQueueType.Next, 1));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 6, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 1), new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [7, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 2), new PlayerIndex(PlayerQueueType.Next, 1));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4, 8]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(4);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 1), new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [7]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(-1);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 0), new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [8, 0, 1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(-1);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 0), new PlayerIndex(PlayerQueueType.Next, 5));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [0, 1, 2, 3, 4, 8]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(-1);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 1), new PlayerIndex(PlayerQueueType.Next, 3));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [0, 1, 2, 8, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [7]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(0);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 2), new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [8, 1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestMove_WaitPrev_noWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 0), new PlayerIndex(PlayerQueueType.Prev, 1));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 5, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 1), new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [6, 0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 3), new PlayerIndex(PlayerQueueType.Prev, 2));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 8]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(4);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 1), new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(4);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [6, 0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(0);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 0), new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [8]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestMove_WaitPrev_withWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 0), new PlayerIndex(PlayerQueueType.Prev, 1));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 6, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 1), new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [7, 0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(1);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 2), new PlayerIndex(PlayerQueueType.Prev, 1));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 8, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(4);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 1), new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [7, 0, 1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(4);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 2), new PlayerIndex(PlayerQueueType.Prev, 5));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3, 4, 8]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(4);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 1), new PlayerIndex(PlayerQueueType.Prev, 4));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3, 7, 4]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(-1);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 0), new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [8]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [0, 1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(1);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 1), new PlayerIndex(PlayerQueueType.Prev, 1));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 8, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [7]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(0);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.User, 2), new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [8, 0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestMove_PrevWait_noWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 0), new PlayerIndex(PlayerQueueType.User, 1));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 0, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 1), new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [1, 5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 2), new PlayerIndex(PlayerQueueType.User, 2));
        CheckCurrentlyPlaying(3);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 2, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(4);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 0), new PlayerIndex(PlayerQueueType.User, 4));
        CheckCurrentlyPlaying(4);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8, 0]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(1);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 0), new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(1);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [0]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestMove_PrevWait_withWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 0), new PlayerIndex(PlayerQueueType.User, 1));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 0, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 1), new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [1, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 2), new PlayerIndex(PlayerQueueType.User, 1));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 2, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(4);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 4), new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [4, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(4);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 4), new PlayerIndex(PlayerQueueType.User, 3));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8, 4]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(0);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 0), new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [0, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(1);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Prev, 1), new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [1]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestMove_NextWait_noWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 0), new PlayerIndex(PlayerQueueType.User, 1));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 3, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 1), new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [4, 5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 0), new PlayerIndex(PlayerQueueType.User, 2));
        CheckCurrentlyPlaying(3);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 4, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(1);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 2), new PlayerIndex(PlayerQueueType.User, 4));
        CheckCurrentlyPlaying(1);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 3]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8, 4]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(1);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 0), new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(1);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [2]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(0);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 0), new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [1, 6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestMove_NextWait_withWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 0), new PlayerIndex(PlayerQueueType.User, 1));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 3, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 1), new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [4, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(1);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 2), new PlayerIndex(PlayerQueueType.User, 1));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 3]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 4, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(0);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 3), new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [4, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(3);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 0), new PlayerIndex(PlayerQueueType.User, 3));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8, 4]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(-1);
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 0), new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [0, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(-1);
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testQueueHandler.MovePlayable(new PlayerIndex(PlayerQueueType.Next, 0), new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [0]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestInsertPodcastQueue_playerModeMusic_NoWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, Array.Empty<int>());
        CheckQueueInfoConsistency();

        testPlayer.InsertPodcastQueue([song9]);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, [9]);
        CheckQueueInfoConsistency();

        testPlayer.InsertPodcastQueue([songA]);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, [9, 10]);
        CheckQueueInfoConsistency();

        testPlayer.InsertPodcastQueue([songB]);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, [9, 11, 10]);
        CheckQueueInfoConsistency();

        testPlayer.InsertPodcastQueue([songC]);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, [9, 12, 11, 10]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestAppendPodcastQueue_playerModeMusic_NoWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);

        testPlayer.AppendPodcastQueue([song9]);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, [9]);
        CheckQueueInfoConsistency();

        testPlayer.AppendPodcastQueue([songA]);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, [9, 10]);
        CheckQueueInfoConsistency();

        testPlayer.AppendPodcastQueue([songB]);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, [9, 10, 11]);
        CheckQueueInfoConsistency();

        testPlayer.AppendPodcastQueue([songC]);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, [9, 10, 11, 12]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestInsertContextQueue_playerModePodcast_NoWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testPlayer.SetPlayerMode(PlayerMode.Podcast);

        CheckCurrentlyPlaying(null);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueItems(testPlayer.ContextQueue.Playables, [0, 1, 2, 3, 4]);
        CheckQueueInfoConsistency();

        testPlayer.InsertContextQueue([song9]);
        CheckCurrentlyPlaying(null);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueItems(testPlayer.ContextQueue.Playables, [0, 1, 2, 9, 3, 4]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Music);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [9, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Podcast);

        testPlayer.InsertContextQueue([songA]);
        CheckCurrentlyPlaying(null);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueItems(testPlayer.ContextQueue.Playables, [0, 1, 2, 10, 9, 3, 4]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Music);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [10, 9, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Podcast);

        testPlayer.InsertContextQueue([songB]);
        CheckCurrentlyPlaying(null);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueItems(testPlayer.ContextQueue.Playables, [0, 1, 2, 11, 10, 9, 3, 4]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Music);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [11, 10, 9, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Podcast);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestAppendContextQueue_playerModePodcast_NoWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testPlayer.SetPlayerMode(PlayerMode.Podcast);

        CheckCurrentlyPlaying(null);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueItems(testPlayer.ContextQueue.Playables, [0, 1, 2, 3, 4]);
        CheckQueueInfoConsistency();

        testPlayer.AppendContextQueue([song9]);
        CheckCurrentlyPlaying(null);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueItems(testPlayer.ContextQueue.Playables, [0, 1, 2, 3, 4, 9]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Music);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4, 9]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Podcast);

        testPlayer.AppendContextQueue([songA]);
        CheckCurrentlyPlaying(null);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueItems(testPlayer.ContextQueue.Playables, [0, 1, 2, 3, 4, 9, 10]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Music);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4, 9, 10]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Podcast);

        testPlayer.AppendContextQueue([songB]);
        CheckCurrentlyPlaying(null);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueItems(testPlayer.ContextQueue.Playables, [0, 1, 2, 3, 4, 9, 10, 11]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Music);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4, 9, 10, 11]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Podcast);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestInsertUserQueue_playerModePodcast_NoWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testPlayer.SetPlayerMode(PlayerMode.Podcast);

        CheckCurrentlyPlaying(null);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueItems(testPlayer.ContextQueue.Playables, [0, 1, 2, 3, 4]);
        CheckQueueInfoConsistency();

        testPlayer.InsertUserQueue([song9]);
        CheckCurrentlyPlaying(null);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueItems(testPlayer.ContextQueue.Playables, [0, 1, 2, 3, 4]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Music);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [9, 5, 6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Podcast);

        testPlayer.InsertUserQueue([songA]);
        CheckCurrentlyPlaying(null);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueItems(testPlayer.ContextQueue.Playables, [0, 1, 2, 3, 4]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Music);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [10, 9, 5, 6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Podcast);

        testPlayer.InsertUserQueue([songB]);
        CheckCurrentlyPlaying(null);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueItems(testPlayer.ContextQueue.Playables, [0, 1, 2, 3, 4]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Music);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [11, 10, 9, 5, 6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Podcast);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestAppendUserQueue_playerModePodcast_NoWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        testPlayer.SetPlayerMode(PlayerMode.Podcast);

        CheckCurrentlyPlaying(null);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueItems(testPlayer.ContextQueue.Playables, [0, 1, 2, 3, 4]);
        CheckQueueInfoConsistency();

        testPlayer.AppendUserQueue([song9]);
        CheckCurrentlyPlaying(null);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueItems(testPlayer.ContextQueue.Playables, [0, 1, 2, 3, 4]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Music);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8, 9]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Podcast);

        testPlayer.AppendUserQueue([songA]);
        CheckCurrentlyPlaying(null);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueItems(testPlayer.ContextQueue.Playables, [0, 1, 2, 3, 4]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Music);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8, 9, 10]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Podcast);

        testPlayer.AppendUserQueue([songB]);
        CheckCurrentlyPlaying(null);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueItems(testPlayer.ContextQueue.Playables, [0, 1, 2, 3, 4]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Music);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8, 9, 10, 11]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Podcast);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestInsertPodcastQueue_playerModeMusic_WithWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, Array.Empty<int>());
        CheckQueueInfoConsistency();

        testPlayer.InsertPodcastQueue([song9]);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, [9]);
        CheckQueueInfoConsistency();

        testPlayer.InsertPodcastQueue([songA]);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, [9, 10]);
        CheckQueueInfoConsistency();

        testPlayer.InsertPodcastQueue([songB]);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, [9, 11, 10]);
        CheckQueueInfoConsistency();

        testPlayer.InsertPodcastQueue([songC]);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, [9, 12, 11, 10]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestAppendPodcastQueue_playerModeMusic_WithWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);

        testPlayer.AppendPodcastQueue([song9]);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, [9]);
        CheckQueueInfoConsistency();

        testPlayer.AppendPodcastQueue([songA]);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, [9, 10]);
        CheckQueueInfoConsistency();

        testPlayer.AppendPodcastQueue([songB]);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, [9, 10, 11]);
        CheckQueueInfoConsistency();

        testPlayer.AppendPodcastQueue([songC]);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueItems(testPlayer.PodcastQueue.Playables, [9, 10, 11, 12]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestInsertContextQueue_playerModePodcast_WithWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);

        testPlayer.SetPlayerMode(PlayerMode.Podcast);
        testPlayer.InsertPodcastQueue([songD, songE, songF]);
        testPlayer.SetCurrentIndex(1);

        CheckCurrentlyPlaying(14);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [13]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [15]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();

        testPlayer.InsertContextQueue([song9]);
        CheckCurrentlyPlaying(14);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [13]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [15]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Music);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [9, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Podcast);

        testPlayer.InsertContextQueue([songA]);
        CheckCurrentlyPlaying(14);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [13]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [15]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Music);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [10, 9, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Podcast);

        testPlayer.InsertContextQueue([songB]);
        CheckCurrentlyPlaying(14);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [13]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [15]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueItems(testPlayer.ContextQueue.Playables, [0, 1, 2, 11, 10, 9, 3, 4]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Music);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [11, 10, 9, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Podcast);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestAppendContextQueue_playerModePodcast_WithWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);

        testPlayer.SetPlayerMode(PlayerMode.Podcast);
        testPlayer.InsertPodcastQueue([songD, songE, songF]);
        testPlayer.SetCurrentIndex(1);

        CheckCurrentlyPlaying(14);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [13]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [15]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();

        testPlayer.AppendContextQueue([song9]);
        CheckCurrentlyPlaying(14);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [13]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [15]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Music);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4, 9]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Podcast);

        testPlayer.AppendContextQueue([songA]);
        CheckCurrentlyPlaying(14);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [13]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [15]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Music);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4, 9, 10]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Podcast);

        testPlayer.AppendContextQueue([songB]);
        CheckCurrentlyPlaying(14);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [13]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [15]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Music);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4, 9, 10, 11]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Podcast);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestInsertUserQueue_playerModePodcast_WithWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);

        testPlayer.SetPlayerMode(PlayerMode.Podcast);
        testPlayer.InsertPodcastQueue([songD, songE, songF]);
        testPlayer.SetCurrentIndex(1);

        CheckCurrentlyPlaying(14);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [13]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [15]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();

        testPlayer.InsertUserQueue([song9]);
        CheckCurrentlyPlaying(14);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [13]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [15]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Music);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [9, 6, 7, 8]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Podcast);

        testPlayer.InsertUserQueue([songA]);
        CheckCurrentlyPlaying(14);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [13]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [15]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Music);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [10, 9, 6, 7, 8]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Podcast);

        testPlayer.InsertUserQueue([songB]);
        CheckCurrentlyPlaying(14);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [13]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [15]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Music);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [11, 10, 9, 6, 7, 8]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Podcast);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestAppendUserQueue_playerModePodcast_WithWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        testPlayer.SetCurrentIndex(2);

        testPlayer.SetPlayerMode(PlayerMode.Podcast);
        testPlayer.InsertPodcastQueue([songD, songE, songF]);
        testPlayer.SetCurrentIndex(1);

        CheckCurrentlyPlaying(14);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [13]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [15]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();

        testPlayer.AppendUserQueue([song9]);
        CheckCurrentlyPlaying(14);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [13]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [15]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Music);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8, 9]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Podcast);

        testPlayer.AppendUserQueue([songA]);
        CheckCurrentlyPlaying(14);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [13]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [15]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Music);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8, 9, 10]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Podcast);

        testPlayer.AppendUserQueue([songB]);
        CheckCurrentlyPlaying(14);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [13]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [15]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Music);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8, 9, 10, 11]);
        CheckQueueInfoConsistency();
        testPlayer.SetPlayerMode(PlayerMode.Podcast);
        await Task.CompletedTask;
    });
}
