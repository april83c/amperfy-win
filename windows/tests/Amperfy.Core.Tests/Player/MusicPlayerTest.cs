// Ported 1:1 from XCTest: keep the original assertion style (count comparisons, force unwraps).
#pragma warning disable xUnit2013, CS8602

using Amperfy.Core.Player;
using Amperfy.Core.Tests.Helper;
using static Amperfy.Core.Tests.Player.TestUtil;

namespace Amperfy.Core.Tests.Player;

/// Port of AmperfyKitTests/Cases/Player/MusicPlayerTest.swift
public class MusicPlayerTest
{
    private readonly CoreDataHelper cdHelper;
    private readonly LibraryStorage library;
    private readonly Account account;
    private readonly AmperfySettings settings;
    private readonly EventLogger eventLogger;
    private readonly UserStatistics userStatistics;
    private readonly MockSongDownloader songDownloader;
    private readonly MockBackendApi backendApi;
    private readonly AlwaysOnlineNetworkMonitor networkMonitor;
    private readonly BackendAudioPlayer backendPlayer;
    private readonly MockMusicPlayable mockMusicPlayable;
    private readonly PlayerData playerData;
    private readonly AudioPlayer testMusicPlayer;
    private readonly IPlayerFacade testPlayer;
    private readonly PlayQueueHandler testQueueHandler;
    private readonly MockAudioStreamingPlayer mockAudioStreamingPlayer;

    private readonly Song songCached;
    private readonly Song songToDownload;
    private readonly Playlist playlistThreeCached;
    private readonly Playlist playlistAllCached;
    private const int fillCount = 5;

    public MusicPlayerTest()
    {
        cdHelper = new CoreDataHelper();
        library = cdHelper.CreateSeededStorage();
        account = library.GetAccount(TestAccountInfo.Create1());
        songDownloader = new MockSongDownloader();
        mockAudioStreamingPlayer = new MockAudioStreamingPlayer();
        settings = new AmperfySettings();
        eventLogger = new EventLogger(library);
        userStatistics = new UserStatistics();
        backendApi = new MockBackendApi();
        networkMonitor = new AlwaysOnlineNetworkMonitor();
        backendPlayer = new BackendAudioPlayer(
            () => mockAudioStreamingPlayer,
            eventLogger,
            _ => backendApi,
            networkMonitor,
            _ => songDownloader,
            library,
            userStatistics)
        {
            TimerFactory = (_, _) => new NoopDisposable(),
        };
        mockMusicPlayable = new MockMusicPlayable();
        playerData = library.GetPlayerData();
        testQueueHandler = new PlayQueueHandler(playerData);
        testMusicPlayer = new AudioPlayer(playerData, testQueueHandler, backendPlayer, settings, userStatistics);
        testPlayer = new PlayerFacadeImpl(playerData, testQueueHandler, testMusicPlayer, library, backendPlayer, userStatistics);
        testPlayer.AddNotifier(mockMusicPlayable);

        songCached = NN(library.GetSong(account, "36"));
        songToDownload = NN(library.GetSong(account, "3"));
        playlistThreeCached = NN(library.GetPlaylist(account, cdHelper.Seeder.Playlists[1].Id));
        playlistAllCached = NN(library.GetPlaylist(account, cdHelper.Seeder.Playlists[3].Id));
    }

    private Account GetAccountForSong(int atIndex)
    {
        var accIndex = cdHelper.Seeder.Songs[atIndex].AccountIndex;
        var accSeed = cdHelper.Seeder.Accounts[accIndex];
        return library.GetAccount(new AccountInfo(accSeed.ServerHash, accSeed.UserHash, accSeed.ApiType));
    }

    private void PrepareWithCachedPlaylist()
    {
        foreach (var song in playlistThreeCached.Playables) testQueueHandler.AppendContextQueue([song]);
    }

    private void PrepareWithAllSongsCached()
    {
        foreach (var song in playlistAllCached.Playables) testQueueHandler.AppendContextQueue([song]);
    }

    private void PrepareNoWaitingQueuePlaying()
    {
        playerData.RemoveAllItems();
        FillPlayerWithSomeSongsAndWaitingQueue();
        playerData.SetUserQueuePlaying(false);
    }

    private void PrepareWithWaitingQueuePlaying()
    {
        PrepareNoWaitingQueuePlaying();
        playerData.SetUserQueuePlaying(true);
    }

    private void FillPlayerWithSomeSongs()
    {
        for (var i = 0; i <= fillCount - 1; i++)
        {
            var song = NN(library.GetSong(GetAccountForSong(i), cdHelper.Seeder.Songs[i].Id));
            testPlayer.AppendContextQueue([song]);
        }
    }

    private void FillPlayerWithSomeSongsAndWaitingQueue()
    {
        FillPlayerWithSomeSongs();
        for (var i = 0; i <= 3; i++)
        {
            var song = NN(library.GetSong(GetAccountForSong(i), cdHelper.Seeder.Songs[fillCount + i].Id));
            testPlayer.AppendUserQueue([song]);
        }
    }

    private void CheckPlaylistIndexEqualSeedIndex(int playlistIndex, int seedIndex)
    {
        var song = NN(library.GetSong(GetAccountForSong(seedIndex), cdHelper.Seeder.Songs[seedIndex].Id));
        Assert.Equal(song.Id, playerData.ContextQueue.Playables[playlistIndex].Id);
    }

    private void CheckQueueItems(IReadOnlyList<AbstractPlayable> queue, int[] seedIds)
    {
        Assert.Equal(seedIds.Length, queue.Count);
        if (queue.Count == seedIds.Length && queue.Count > 0)
        {
            for (var i = 0; i <= queue.Count - 1; i++)
            {
                var song = NN(library.GetSong(GetAccountForSong(seedIds[i]), cdHelper.Seeder.Songs[seedIds[i]].Id));
                Assert.Equal(song.Id, queue[i].Id);
            }
        }
    }

    private void CheckCurrentlyPlaying(int? idToBe)
    {
        if (idToBe is { } id)
        {
            var song = NN(library.GetSong(GetAccountForSong(id), cdHelper.Seeder.Songs[id].Id));
            Assert.Equal(song.Id, testQueueHandler.CurrentlyPlaying?.Id);
        }
        else
        {
            Assert.Null(testQueueHandler.CurrentlyPlaying);
        }
    }

    private void CheckQueueInfoConsistency() => TestUtil.CheckQueueInfoConsistency(testQueueHandler);

    // -------------------------------------------------------------

    [Fact]
    public void TestCreation() => Run(async () =>
    {
        Assert.False(testPlayer.IsPlaying);
        Assert.False(testPlayer.IsShuffle);
        Assert.Null(testPlayer.CurrentlyPlaying);
        Assert.Empty(testPlayer.GetAllPrevQueueItems());
        Assert.Empty(testPlayer.GetAllNextQueueItems());
        Assert.Empty(testPlayer.GetAllUserQueueItems());
        Assert.Equal(0.0, testPlayer.ElapsedTime);
        Assert.Equal(0.0, testPlayer.Duration);
        Assert.Equal(RepeatMode.Off, testPlayer.RepeatMode);
        Assert.True(songDownloader.IsNoDownloadRequested());
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlay_EmptyPlaylist() => Run(async () =>
    {
        testPlayer.Play();
        Assert.False(testPlayer.IsPlaying);
        Assert.Null(testPlayer.CurrentlyPlaying);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlay_OneCachedSongInPlayer_IsPlayingTrue() => Run(async () =>
    {
        testPlayer.AppendContextQueue([songCached]);
        testPlayer.Play();
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(songCached, testPlayer.CurrentlyPlaying);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlay_OneCachedSongInPlayer_NoDownloadRequest() => Run(async () =>
    {
        testPlayer.AppendContextQueue([songCached]);
        testPlayer.Play();
        Assert.True(songDownloader.IsNoDownloadRequested());
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlay_OneSongToDownload_IsPlayingFalse_UntilDownloadfinishes() => Run(async () =>
    {
        mockMusicPlayable.Expect(1);
        testPlayer.AppendContextQueue([songToDownload]);
        testPlayer.Play();
        Assert.False(testPlayer.IsPlaying);
        await mockMusicPlayable.WaitAsync(2.0);
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(songToDownload, testPlayer.CurrentlyPlaying);
    });

    [Fact]
    public void TestPlay_OneSongToDownload_CheckDownloadRequest() => Run(async () =>
    {
        mockMusicPlayable.Expect(1);
        testPlayer.IsAutoCachePlayedItems = true;
        testPlayer.AppendContextQueue([songToDownload]);
        testPlayer.Play();
        Assert.Equal(0, songDownloader.Downloadables.Count);
        await mockMusicPlayable.WaitAsync(2.0);
        Assert.Equal(1, songDownloader.Downloadables.Count);
        Assert.Equal(songToDownload, ((AbstractPlayable)songDownloader.Downloadables.First()).AsSong!);
    });

    [Fact]
    public void TestPlaySong_Cached() => Run(async () =>
    {
        testPlayer.Play(new PlayContext("", [songCached]));
        Assert.Equal(songCached, testPlayer.CurrentlyPlaying);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestContextName() => Run(async () =>
    {
        testPlayer.Play(new PlayContext("asdf", [songCached]));
        Assert.Equal("asdf", testPlayer.ContextName);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestContextName_changeContext() => Run(async () =>
    {
        testPlayer.Play(new PlayContext("asdf", [songCached]));
        Assert.Equal("asdf", testPlayer.ContextName);
        testPlayer.Play(new PlayContext("uio qwe", [songCached]));
        Assert.Equal("uio qwe", testPlayer.ContextName);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestContextName_insertContext() => Run(async () =>
    {
        testPlayer.Play(new PlayContext("asdf", [songCached]));
        testPlayer.InsertContextQueue([songCached]);
        Assert.Equal("Mixed Context", testPlayer.ContextName);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestContextName_appendContext() => Run(async () =>
    {
        testPlayer.Play(new PlayContext("asdf", [songCached]));
        testPlayer.AppendContextQueue([songCached]);
        Assert.Equal("Mixed Context", testPlayer.ContextName);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestContextName_insertUser() => Run(async () =>
    {
        testPlayer.Play(new PlayContext("asdf", [songCached]));
        testPlayer.InsertUserQueue([songCached]);
        Assert.Equal("asdf", testPlayer.ContextName);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestContextName_appendUser() => Run(async () =>
    {
        testPlayer.Play(new PlayContext("asdf", [songCached]));
        testPlayer.AppendUserQueue([songCached]);
        Assert.Equal("asdf", testPlayer.ContextName);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlaySong_CheckPlaylistClear() => Run(async () =>
    {
        PrepareWithCachedPlaylist();
        testPlayer.Play(new PlayContext("", [songCached]));
        Assert.Empty(testPlayer.GetAllPrevQueueItems());
        Assert.Empty(testPlayer.GetAllUserQueueItems());
        Assert.Empty(testPlayer.GetAllNextQueueItems());
        Assert.Equal(songCached, testPlayer.CurrentlyPlaying);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlaySongInPlaylistAt_EmptyPlaylist() => Run(async () =>
    {
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Prev, 0));
        Assert.False(testPlayer.IsPlaying);
        Assert.Null(testPlayer.CurrentlyPlaying);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 5));
        Assert.False(testPlayer.IsPlaying);
        Assert.Null(testPlayer.CurrentlyPlaying);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Prev, -1));
        Assert.False(testPlayer.IsPlaying);
        Assert.Null(testPlayer.CurrentlyPlaying);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlaySongInPlaylistAt_Cached_FullPlaylist() => Run(async () =>
    {
        PrepareWithCachedPlaylist();
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 2));
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(3, playerData.CurrentIndex);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlaySongInPlaylistAt_FetchSuccess_FullPlaylist() => Run(async () =>
    {
        mockMusicPlayable.Expect(1);
        PrepareWithCachedPlaylist();
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 1));
        await mockMusicPlayable.WaitAsync(2.0);
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(2, playerData.CurrentIndex);
    });

    [Fact]
    public void TestPause_EmptyPlaylist() => Run(async () =>
    {
        testMusicPlayer.Pause();
        Assert.False(testPlayer.IsPlaying);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPause_CurrentlyPlaying() => Run(async () =>
    {
        testPlayer.AppendContextQueue([songCached]);
        testPlayer.Play();
        testMusicPlayer.Pause();
        Assert.False(testPlayer.IsPlaying);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPause_CurrentlyPaused() => Run(async () =>
    {
        testPlayer.AppendContextQueue([songCached]);
        testMusicPlayer.Pause();
        Assert.False(testPlayer.IsPlaying);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPause_SongInMiddleOfPlaylist() => Run(async () =>
    {
        PrepareWithCachedPlaylist();
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 2));
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(3, playerData.CurrentIndex);
        testMusicPlayer.Pause();
        Assert.False(testPlayer.IsPlaying);
        Assert.Equal(3, playerData.CurrentIndex);
        testPlayer.Play();
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(3, playerData.CurrentIndex);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestAddToPlaylist() => Run(async () =>
    {
        Assert.Equal(0, testPlayer.GetAllNextQueueItems().Count);
        testPlayer.AppendContextQueue([songCached]);
        Assert.Equal(0, testPlayer.GetAllNextQueueItems().Count);
        testPlayer.AppendContextQueue([songToDownload]);
        Assert.Equal(1, testPlayer.GetAllNextQueueItems().Count);
        testPlayer.AppendContextQueue([songCached]);
        Assert.Equal(2, testPlayer.GetAllNextQueueItems().Count);
        testPlayer.AppendContextQueue([songToDownload]);
        Assert.Equal(3, testPlayer.GetAllNextQueueItems().Count);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlaylistClear_EmptyPlaylist() => Run(async () =>
    {
        testPlayer.ClearContextQueue();
        Assert.False(testPlayer.IsPlaying);
        Assert.Equal(0, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Null(testPlayer.CurrentlyPlaying);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlaylistClear_EmptyPlaylist_WaitingQueueHasEntries() => Run(async () =>
    {
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        testPlayer.AppendUserQueue([songId0]);
        Assert.False(testPlayer.IsPlaying);
        testPlayer.ClearContextQueue();
        Assert.False(testPlayer.IsPlaying);
        Assert.Equal(0, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId0, testPlayer.CurrentlyPlaying);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlaylistClear_EmptyPlaylist_WaitingQueueHasEntries2() => Run(async () =>
    {
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        testPlayer.AppendUserQueue([songId0]);
        var songId1 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[1].Id));
        testPlayer.AppendUserQueue([songId1]);
        Assert.False(testPlayer.IsPlaying);
        testPlayer.ClearContextQueue();
        Assert.False(testPlayer.IsPlaying);
        Assert.Equal(0, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(1, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId0, testPlayer.CurrentlyPlaying);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlaylistClear_EmptyPlaylist_WaitingQueueHasEntries3() => Run(async () =>
    {
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        testPlayer.InsertUserQueue([songId0]);
        Assert.False(testPlayer.IsPlaying);
        testPlayer.ClearContextQueue();
        Assert.False(testPlayer.IsPlaying);
        Assert.Equal(0, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId0, testPlayer.CurrentlyPlaying);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlaylistClear_EmptyPlaylist_WaitingQueueHasEntries4() => Run(async () =>
    {
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        testPlayer.InsertUserQueue([songId0]);
        var songId1 = NN(library.GetSong(GetAccountForSong(1), cdHelper.Seeder.Songs[1].Id));
        testPlayer.InsertUserQueue([songId1]);
        Assert.False(testPlayer.IsPlaying);
        testPlayer.ClearContextQueue();
        Assert.False(testPlayer.IsPlaying);
        Assert.Equal(0, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(1, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId0, testPlayer.CurrentlyPlaying);
        Assert.Equal(songId1, testPlayer.GetAllUserQueueItems()[0]);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlaylistClear_EmptyPlaylist_WaitingQueueHasEntries5() => Run(async () =>
    {
        mockMusicPlayable.Expect(1);
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        testPlayer.AppendUserQueue([songId0]);
        var songId1 = NN(library.GetSong(GetAccountForSong(1), cdHelper.Seeder.Songs[1].Id));
        testPlayer.AppendUserQueue([songId1]);
        testPlayer.Play();
        await mockMusicPlayable.WaitAsync(3.0);
        Assert.True(testPlayer.IsPlaying);
        testPlayer.ClearContextQueue();
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(0, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(1, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId0, testPlayer.CurrentlyPlaying);
    });

    [Fact]
    public void TestPlaylistClear_FilledPlaylist() => Run(async () =>
    {
        PrepareWithCachedPlaylist();
        testPlayer.ClearContextQueue();
        Assert.False(testPlayer.IsPlaying);
        Assert.Equal(0, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Null(testPlayer.CurrentlyPlaying);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlaylistClear_FilledPlaylist_WaitingQueuePlaying() => Run(async () =>
    {
        mockMusicPlayable.Expect(1);
        PrepareWithCachedPlaylist();
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        testPlayer.AppendUserQueue([songId0]);
        var songId1 = NN(library.GetSong(GetAccountForSong(1), cdHelper.Seeder.Songs[1].Id));
        testPlayer.AppendUserQueue([songId1]);
        testPlayer.ClearContextQueue();
        await mockMusicPlayable.WaitAsync(2.0);
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(0, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(1, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId0, testPlayer.CurrentlyPlaying);
    });

    [Fact]
    public void TestPlaylistClear_FilledPlaylist_WaitingQueuePlaying2() => Run(async () =>
    {
        mockMusicPlayable.Expect(1);
        PrepareWithCachedPlaylist();
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        testPlayer.AppendUserQueue([songId0]);
        var songId1 = NN(library.GetSong(GetAccountForSong(1), cdHelper.Seeder.Songs[1].Id));
        testPlayer.AppendUserQueue([songId1]);
        testPlayer.PlayNext();
        testPlayer.ClearContextQueue();
        await mockMusicPlayable.WaitAsync(2.0);
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(0, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(1, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId0, testPlayer.CurrentlyPlaying);
    });

    [Fact]
    public void TestPlayMulitpleSongs_WaitingQueuePlaying() => Run(async () =>
    {
        mockMusicPlayable.Expect(1);
        mockMusicPlayable.Expect(2);
        PrepareWithCachedPlaylist();
        playerData.SetCurrentIndex(1);
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        testPlayer.AppendUserQueue([songId0]);
        testPlayer.PlayNext();

        testPlayer.ClearContextQueue();
        var songId1 = NN(library.GetSong(GetAccountForSong(1), cdHelper.Seeder.Songs[1].Id));
        var songId2 = NN(library.GetSong(account, cdHelper.Seeder.Songs[2].Id));
        var songId3 = NN(library.GetSong(account, cdHelper.Seeder.Songs[3].Id));
        testPlayer.AppendContextQueue([songId1, songId2, songId3]);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 0));
        await mockMusicPlayable.WaitAsync(2.0);
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(0, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(2, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId1, testPlayer.CurrentlyPlaying);
    });

    [Fact]
    public void TestPlayMulitpleSongs_WaitingQueuePlaying2() => Run(async () =>
    {
        mockMusicPlayable.Expect(1);
        mockMusicPlayable.Expect(2);
        PrepareWithCachedPlaylist();
        playerData.SetCurrentIndex(1);
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        testPlayer.AppendUserQueue([songId0]);
        testPlayer.PlayNext();

        var songId1 = NN(library.GetSong(GetAccountForSong(1), cdHelper.Seeder.Songs[1].Id));
        var songId2 = NN(library.GetSong(account, cdHelper.Seeder.Songs[2].Id));
        var songId3 = NN(library.GetSong(account, cdHelper.Seeder.Songs[3].Id));
        testPlayer.Play(new PlayContext("", [songId1, songId2, songId3]));
        await mockMusicPlayable.WaitAsync(2.0);
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(0, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(2, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId1, testPlayer.CurrentlyPlaying);
    });

    [Fact]
    public void TestPlayMulitpleSongs_WaitingQueuePlaying8() => Run(async () =>
    {
        mockMusicPlayable.Expect(1);
        PrepareWithCachedPlaylist();
        playerData.SetCurrentIndex(1);
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        testPlayer.AppendUserQueue([songId0]);

        var songId1 = NN(library.GetSong(GetAccountForSong(1), cdHelper.Seeder.Songs[1].Id));
        var songId2 = NN(library.GetSong(account, cdHelper.Seeder.Songs[2].Id));
        var songId3 = NN(library.GetSong(account, cdHelper.Seeder.Songs[3].Id));
        testPlayer.Play(new PlayContext("", [songId1, songId2, songId3]));
        await mockMusicPlayable.WaitAsync(2.0);
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(0, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(1, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(2, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId1, testPlayer.CurrentlyPlaying);
    });

    [Fact]
    public void TestPlaySong_WaitingQueuePlaying8() => Run(async () =>
    {
        PrepareWithCachedPlaylist();
        playerData.SetCurrentIndex(1);
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        testPlayer.AppendUserQueue([songId0]);

        var songId1 = NN(library.GetSong(GetAccountForSong(1), cdHelper.Seeder.Songs[1].Id));
        testPlayer.Play(new PlayContext("", [songId1]));
        Assert.Equal(0, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(1, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId1, testPlayer.CurrentlyPlaying);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlaySong_WaitingQueuePlaying9() => Run(async () =>
    {
        mockMusicPlayable.Expect(1);
        mockMusicPlayable.Expect(2);
        PrepareWithCachedPlaylist();
        playerData.SetCurrentIndex(1);
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        var songId1 = NN(library.GetSong(GetAccountForSong(1), cdHelper.Seeder.Songs[1].Id));
        var songId2 = NN(library.GetSong(account, cdHelper.Seeder.Songs[2].Id));
        testPlayer.AppendUserQueue([songId0]);
        testPlayer.AppendUserQueue([songId1]);
        testPlayer.AppendUserQueue([songId2]);
        testPlayer.PlayNext();

        var songId3 = NN(library.GetSong(account, cdHelper.Seeder.Songs[3].Id));
        testPlayer.Play(new PlayContext("", [songId3]));
        await mockMusicPlayable.WaitAsync(3.0);
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(0, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(2, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId3, testPlayer.CurrentlyPlaying);
    });

    [Fact]
    public void TestPlayMulitpleSongs_WaitingQueuePlaying3() => Run(async () =>
    {
        PrepareWithCachedPlaylist();
        playerData.SetCurrentIndex(1);
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        var songId1 = NN(library.GetSong(GetAccountForSong(1), cdHelper.Seeder.Songs[1].Id));
        var songId2 = NN(library.GetSong(account, cdHelper.Seeder.Songs[2].Id));
        testPlayer.AppendUserQueue([songId0]);
        testPlayer.AppendUserQueue([songId1]);
        testPlayer.AppendUserQueue([songId2]);
        testPlayer.PlayNext();

        var songId3 = NN(library.GetSong(account, cdHelper.Seeder.Songs[3].Id));
        testPlayer.Play(new PlayContext("", [songId3]));
        Assert.Equal(0, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(2, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId3, testPlayer.CurrentlyPlaying);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayMulitpleSongs_WaitingQueuePlaying4() => Run(async () =>
    {
        PrepareWithCachedPlaylist();
        playerData.SetCurrentIndex(1);
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        testPlayer.AppendUserQueue([songId0]);
        testPlayer.PlayNext();

        var songId1 = NN(library.GetSong(GetAccountForSong(1), cdHelper.Seeder.Songs[1].Id));
        testPlayer.Play(new PlayContext("", [songId1]));
        Assert.Equal(0, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId1, testPlayer.CurrentlyPlaying);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayMulitpleSongs_WaitingQueuePlaying5() => Run(async () =>
    {
        mockMusicPlayable.Expect(1);
        mockMusicPlayable.Expect(2);
        PrepareWithCachedPlaylist();
        playerData.SetCurrentIndex(1);
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        testPlayer.AppendUserQueue([songId0]);
        testPlayer.PlayNext();

        var songId1 = NN(library.GetSong(GetAccountForSong(1), cdHelper.Seeder.Songs[1].Id));
        testPlayer.Play(new PlayContext("", [songId1]));
        Assert.False(testPlayer.IsPlaying);
        await mockMusicPlayable.WaitAsync(2.0);
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(0, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId1, testPlayer.CurrentlyPlaying);
    });

    [Fact]
    public void TestPlayMulitpleSongs_WaitingQueuePlaying6() => Run(async () =>
    {
        PrepareWithCachedPlaylist();
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        testPlayer.Play(new PlayContext("", [songId0]));

        var songId1 = NN(library.GetSong(GetAccountForSong(1), cdHelper.Seeder.Songs[1].Id));
        testPlayer.Play(new PlayContext("", [songId1]));
        Assert.Equal(0, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId1.Id, testPlayer.CurrentlyPlaying?.Id);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayMulitpleSongs_userQueueHasElements_notUserQueuePlaying() => Run(async () =>
    {
        playerData.RemoveAllItems();
        playerData.SetUserQueuePlaying(false);
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        testPlayer.AppendUserQueue([songId0]);

        var songId1 = NN(library.GetSong(GetAccountForSong(1), cdHelper.Seeder.Songs[1].Id));
        testPlayer.Play(new PlayContext("", [songId1]));
        Assert.Equal(0, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId1.Id, testPlayer.CurrentlyPlaying?.Id);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayMulitpleSongs_userQueueHasElements_notUserQueuePlaying2() => Run(async () =>
    {
        playerData.RemoveAllItems();
        playerData.SetUserQueuePlaying(false);
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        testPlayer.AppendUserQueue([songId0]);

        var songId1 = NN(library.GetSong(GetAccountForSong(1), cdHelper.Seeder.Songs[1].Id));
        var songId2 = NN(library.GetSong(account, cdHelper.Seeder.Songs[2].Id));
        var songId3 = NN(library.GetSong(account, cdHelper.Seeder.Songs[3].Id));
        testPlayer.Play(new PlayContext("", [songId1, songId2, songId3]));
        Assert.Equal(0, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(2, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId1.Id, testPlayer.CurrentlyPlaying?.Id);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayMulitpleSongs_userQueueHasElements_notUserQueuePlaying3() => Run(async () =>
    {
        playerData.RemoveAllItems();
        playerData.SetUserQueuePlaying(false);
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        testPlayer.AppendUserQueue([songId0]);

        var songId1 = NN(library.GetSong(GetAccountForSong(1), cdHelper.Seeder.Songs[1].Id));
        var songId2 = NN(library.GetSong(account, cdHelper.Seeder.Songs[2].Id));
        var songId3 = NN(library.GetSong(account, cdHelper.Seeder.Songs[3].Id));
        testPlayer.Play(new PlayContext("", 1, [songId1, songId2, songId3]));
        Assert.Equal(1, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(1, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId2.Id, testPlayer.CurrentlyPlaying?.Id);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayMulitpleSongs_userQueueHasElements_notUserQueuePlaying4() => Run(async () =>
    {
        mockMusicPlayable.Expect(1);
        playerData.RemoveAllItems();
        playerData.SetUserQueuePlaying(false);
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        var songId4 = NN(library.GetSong(account, cdHelper.Seeder.Songs[4].Id));
        var songId5 = NN(library.GetSong(account, cdHelper.Seeder.Songs[5].Id));
        testPlayer.AppendUserQueue([songId0, songId4, songId5]);

        var songId1 = NN(library.GetSong(GetAccountForSong(1), cdHelper.Seeder.Songs[1].Id));
        var songId2 = NN(library.GetSong(account, cdHelper.Seeder.Songs[2].Id));
        var songId3 = NN(library.GetSong(account, cdHelper.Seeder.Songs[3].Id));
        testPlayer.Play(new PlayContext("", 2, [songId1, songId2, songId3]));
        Assert.False(testPlayer.IsPlaying);
        await mockMusicPlayable.WaitAsync(2.0);
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(2, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(2, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId3.Id, testPlayer.CurrentlyPlaying?.Id);
        Assert.Equal(songId4.Id, testPlayer.GetAllUserQueueItems()[0].Id);
        Assert.Equal(songId5.Id, testPlayer.GetAllUserQueueItems()[1].Id);
    });

    [Fact]
    public void TestPlayMulitpleSongs_switchPlayerMode_toMusic() => Run(async () =>
    {
        playerData.RemoveAllItems();
        playerData.SetUserQueuePlaying(false);
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        var songId4 = NN(library.GetSong(account, cdHelper.Seeder.Songs[4].Id));
        var songId5 = NN(library.GetSong(account, cdHelper.Seeder.Songs[5].Id));
        testPlayer.AppendContextQueue([songId0, songId4, songId5]);

        var songId1 = NN(library.GetSong(GetAccountForSong(1), cdHelper.Seeder.Songs[1].Id));
        var songId2 = NN(library.GetSong(account, cdHelper.Seeder.Songs[2].Id));
        var songId3 = NN(library.GetSong(account, cdHelper.Seeder.Songs[3].Id));
        testPlayer.InsertPodcastQueue([songId1, songId2, songId3]);
        testPlayer.SetPlayerMode(PlayerMode.Podcast);

        var songId7 = NN(library.GetSong(account, cdHelper.Seeder.Songs[7].Id));
        var songId8 = NN(library.GetSong(account, cdHelper.Seeder.Songs[8].Id));
        var songId9 = NN(library.GetSong(account, cdHelper.Seeder.Songs[9].Id));
        testPlayer.Play(new PlayContext("", 1, [songId7, songId8, songId9]));
        Assert.Equal(PlayerMode.Music, testPlayer.PlayerMode);
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(1, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(1, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId8.Id, testPlayer.CurrentlyPlaying?.Id);
        Assert.Equal(songId7.Id, testPlayer.GetAllPrevQueueItems()[0].Id);
        Assert.Equal(songId9.Id, testPlayer.GetAllNextQueueItems()[0].Id);

        testPlayer.SetPlayerMode(PlayerMode.Podcast);
        Assert.Equal(0, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(2, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId1.Id, testPlayer.CurrentlyPlaying?.Id);
        Assert.Equal(songId2.Id, testPlayer.GetAllNextQueueItems()[0].Id);
        Assert.Equal(songId3.Id, testPlayer.GetAllNextQueueItems()[1].Id);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayMulitpleSongs_switchPlayerMode_toMusic2() => Run(async () =>
    {
        playerData.RemoveAllItems();
        playerData.SetUserQueuePlaying(false);
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        var songId4 = NN(library.GetSong(account, cdHelper.Seeder.Songs[4].Id));
        var songId5 = NN(library.GetSong(account, cdHelper.Seeder.Songs[5].Id));
        testPlayer.AppendContextQueue([songId0, songId4, songId5]);
        playerData.SetCurrentIndex(0);

        testPlayer.SetPlayerMode(PlayerMode.Podcast);
        var songId1 = NN(library.GetSong(GetAccountForSong(1), cdHelper.Seeder.Songs[1].Id));
        var songId2 = NN(library.GetSong(account, cdHelper.Seeder.Songs[2].Id));
        var songId3 = NN(library.GetSong(account, cdHelper.Seeder.Songs[3].Id));
        testPlayer.InsertPodcastQueue([songId1, songId2, songId3]);
        playerData.SetCurrentIndex(1);

        var songId7 = NN(library.GetSong(account, cdHelper.Seeder.Songs[7].Id));
        var songId8 = NN(library.GetSong(account, cdHelper.Seeder.Songs[8].Id));
        var songId9 = NN(library.GetSong(account, cdHelper.Seeder.Songs[9].Id));
        testPlayer.Play(new PlayContext("", PlayerMode.Music, 2, [songId7, songId8, songId9]));
        Assert.Equal(PlayerMode.Music, testPlayer.PlayerMode);
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(2, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId9.Id, testPlayer.CurrentlyPlaying?.Id);
        Assert.Equal(songId7.Id, testPlayer.GetAllPrevQueueItems()[0].Id);
        Assert.Equal(songId8.Id, testPlayer.GetAllPrevQueueItems()[1].Id);

        testPlayer.SetPlayerMode(PlayerMode.Podcast);
        Assert.Equal(1, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(1, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId2.Id, testPlayer.CurrentlyPlaying?.Id);
        Assert.Equal(songId1.Id, testPlayer.GetAllPrevQueueItems()[0].Id);
        Assert.Equal(songId3.Id, testPlayer.GetAllNextQueueItems()[0].Id);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayMulitpleSongs_switchPlayerMode_toPodcast() => Run(async () =>
    {
        playerData.RemoveAllItems();
        playerData.SetUserQueuePlaying(false);
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        var songId4 = NN(library.GetSong(account, cdHelper.Seeder.Songs[4].Id));
        var songId5 = NN(library.GetSong(account, cdHelper.Seeder.Songs[5].Id));
        testPlayer.AppendContextQueue([songId0, songId4, songId5]);

        var songId1 = NN(library.GetSong(GetAccountForSong(1), cdHelper.Seeder.Songs[1].Id));
        var songId2 = NN(library.GetSong(account, cdHelper.Seeder.Songs[2].Id));
        var songId3 = NN(library.GetSong(account, cdHelper.Seeder.Songs[3].Id));
        testPlayer.InsertPodcastQueue([songId1, songId2, songId3]);

        var songId7 = NN(library.GetSong(account, cdHelper.Seeder.Songs[7].Id));
        var songId8 = NN(library.GetSong(account, cdHelper.Seeder.Songs[8].Id));
        var songId9 = NN(library.GetSong(account, cdHelper.Seeder.Songs[9].Id));
        testPlayer.Play(new PlayContext("", PlayerMode.Podcast, 1, [songId7, songId8, songId9]));
        Assert.Equal(PlayerMode.Podcast, testPlayer.PlayerMode);
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(1, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(1, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId8.Id, testPlayer.CurrentlyPlaying?.Id);
        Assert.Equal(songId7.Id, testPlayer.GetAllPrevQueueItems()[0].Id);
        Assert.Equal(songId9.Id, testPlayer.GetAllNextQueueItems()[0].Id);

        testPlayer.SetPlayerMode(PlayerMode.Music);
        Assert.Equal(0, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(2, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId0.Id, testPlayer.CurrentlyPlaying?.Id);
        Assert.Equal(songId4.Id, testPlayer.GetAllNextQueueItems()[0].Id);
        Assert.Equal(songId5.Id, testPlayer.GetAllNextQueueItems()[1].Id);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayMulitpleSongs_switchPlayerMode_toPodcast2() => Run(async () =>
    {
        playerData.RemoveAllItems();
        playerData.SetUserQueuePlaying(false);
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        var songId4 = NN(library.GetSong(account, cdHelper.Seeder.Songs[4].Id));
        var songId5 = NN(library.GetSong(account, cdHelper.Seeder.Songs[5].Id));
        testPlayer.AppendContextQueue([songId0, songId4, songId5]);
        playerData.SetCurrentIndex(1);

        testPlayer.SetPlayerMode(PlayerMode.Podcast);
        var songId1 = NN(library.GetSong(GetAccountForSong(1), cdHelper.Seeder.Songs[1].Id));
        var songId2 = NN(library.GetSong(account, cdHelper.Seeder.Songs[2].Id));
        var songId3 = NN(library.GetSong(account, cdHelper.Seeder.Songs[3].Id));
        testPlayer.InsertPodcastQueue([songId1, songId2, songId3]);
        playerData.SetCurrentIndex(0);

        var songId7 = NN(library.GetSong(account, cdHelper.Seeder.Songs[7].Id));
        var songId8 = NN(library.GetSong(account, cdHelper.Seeder.Songs[8].Id));
        var songId9 = NN(library.GetSong(account, cdHelper.Seeder.Songs[9].Id));
        testPlayer.Play(new PlayContext("", PlayerMode.Podcast, 2, [songId7, songId8, songId9]));
        Assert.Equal(PlayerMode.Podcast, testPlayer.PlayerMode);
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(2, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId9.Id, testPlayer.CurrentlyPlaying?.Id);
        Assert.Equal(songId7.Id, testPlayer.GetAllPrevQueueItems()[0].Id);
        Assert.Equal(songId8.Id, testPlayer.GetAllPrevQueueItems()[1].Id);

        testPlayer.SetPlayerMode(PlayerMode.Music);
        Assert.Equal(1, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(1, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(songId4.Id, testPlayer.CurrentlyPlaying?.Id);
        Assert.Equal(songId0.Id, testPlayer.GetAllPrevQueueItems()[0].Id);
        Assert.Equal(songId5.Id, testPlayer.GetAllNextQueueItems()[0].Id);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlay_ContextNameChanges() => Run(async () =>
    {
        playerData.RemoveAllItems();
        var songId0 = NN(library.GetSong(GetAccountForSong(0), cdHelper.Seeder.Songs[0].Id));
        var songId1 = NN(library.GetSong(GetAccountForSong(1), cdHelper.Seeder.Songs[1].Id));

        testPlayer.Play(new PlayContext("Blub", [songId0, songId1]));
        Assert.Equal("Blub", testPlayer.ContextName);
        testPlayer.SetPlayerMode(PlayerMode.Podcast);
        Assert.Equal("Podcasts", testPlayer.ContextName);
        testPlayer.AppendContextQueue([songId0]);
        Assert.Equal("Podcasts", testPlayer.ContextName);
        testPlayer.SetPlayerMode(PlayerMode.Music);
        Assert.Equal("Mixed Context", testPlayer.ContextName);

        testPlayer.Play(new PlayContext("YYYY", [songId0, songId1]));
        Assert.Equal("YYYY", testPlayer.ContextName);
        testPlayer.Play(new PlayContext("GoGo Podcasts", PlayerMode.Podcast, [songId0, songId1]));
        Assert.Equal("Podcasts", testPlayer.ContextName);
        testPlayer.SetPlayerMode(PlayerMode.Music);
        Assert.Equal("YYYY", testPlayer.ContextName);

        testPlayer.Play(new PlayContext("BBBB", [songId0, songId1]));
        Assert.Equal("BBBB", testPlayer.ContextName);
        testPlayer.AppendPodcastQueue([songId0]);
        Assert.Equal("BBBB", testPlayer.ContextName);
        testPlayer.SetPlayerMode(PlayerMode.Podcast);
        Assert.Equal("Podcasts", testPlayer.ContextName);
        testPlayer.InsertPodcastQueue([songId0]);
        testPlayer.SetPlayerMode(PlayerMode.Music);
        Assert.Equal("BBBB", testPlayer.ContextName);

        testPlayer.SetPlayerMode(PlayerMode.Music);
        testPlayer.InsertContextQueue([songId0]);
        Assert.Equal("Mixed Context", testPlayer.ContextName);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestSeek_EmptyPlaylist() => Run(async () =>
    {
        testPlayer.Seek(3.0);
        Assert.Equal(0.0, testPlayer.ElapsedTime);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestSeek_FilledPlaylist() => Run(async () =>
    {
        testPlayer.Play(new PlayContext("", [songCached]));
        testPlayer.Seek(3.0);
        backendPlayer.DidStartPlaying(BackendAudioPlayer.GetFileUrl(library.GetFilePath(testPlayer.CurrentlyPlaying!)!));
        var elapsedTime = testPlayer.ElapsedTime;
        Assert.Equal(3.0, elapsedTime);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayPreviousOrReplay_EmptyPlaylist() => Run(async () =>
    {
        testPlayer.PlayPreviousOrReplay();
        Assert.Null(testPlayer.CurrentlyPlaying);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayPreviousOrReplay_Previous() => Run(async () =>
    {
        PrepareWithCachedPlaylist();
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 2));
        testPlayer.PlayPreviousOrReplay();
        Assert.Equal(2, playerData.CurrentIndex);
        Assert.Equal(2, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(6, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(0.0, testPlayer.ElapsedTime);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayPreviousOrReplay_Replay() => Run(async () =>
    {
        PrepareWithCachedPlaylist();
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 2));
        Assert.False(testPlayer.IsStopInsteadOfPause);
        testPlayer.Seek(10.0);

        backendPlayer.DidStartPlaying(BackendAudioPlayer.GetFileUrl(library.GetFilePath(testPlayer.CurrentlyPlaying!)!));

        testPlayer.PlayPreviousOrReplay();
        Assert.True(testPlayer.IsSkipAvailable);
        Assert.Equal(3, playerData.CurrentIndex);
        Assert.Equal(3, testPlayer.GetAllPrevQueueItems().Count);
        Assert.Equal(0, testPlayer.GetAllUserQueueItems().Count);
        Assert.Equal(5, testPlayer.GetAllNextQueueItems().Count);
        CheckQueueInfoConsistency();
        Assert.Equal(0.0, testPlayer.ElapsedTime);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayPreviousOrReplay_Radio_Previous() => Run(async () =>
    {
        var radios = library.GetRadios(account);
        Assert.Equal(4, radios.Count);
        testPlayer.Play(new PlayContext("Radios", radios));
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 2));
        testPlayer.Seek(10.0);
        testPlayer.PlayPreviousOrReplay();
        Assert.Equal(2, playerData.CurrentIndex);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPauseRadio() => Run(async () =>
    {
        mockMusicPlayable.Expect(1);
        mockMusicPlayable.Expect(2);

        var radios = library.GetRadios(account);
        Assert.Equal(4, radios.Count);
        testPlayer.Play(new PlayContext("Radios", radios));
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 2));
        await mockMusicPlayable.WaitAsync(2.0);
        Assert.True(testPlayer.IsStopInsteadOfPause);
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(3, playerData.CurrentIndex);
        testPlayer.Pause();
        Assert.False(testPlayer.IsPlaying);
        Assert.False(testPlayer.IsSkipAvailable);
        Assert.Equal(3, playerData.CurrentIndex);
    });

    [Fact]
    public void TestRadioInvalidUrl() => Run(async () =>
    {
        var radios = library.GetRadios(account);

        mockMusicPlayable.Expect(1);
        mockMusicPlayable.Expect(1);
        testPlayer.Play(new PlayContext("Radios", radios));
        await mockMusicPlayable.WaitAsync(2.0);

        mockMusicPlayable.Expect(1);
        mockMusicPlayable.Expect(1);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 0));
        await mockMusicPlayable.WaitAsync(2.0);

        Assert.True(testPlayer.IsStopInsteadOfPause);
        Assert.False(testPlayer.IsPlaying);
        Assert.Equal(1, playerData.CurrentIndex);
    });

    [Fact]
    public void TestPlayPrevious_EmptyPlaylist() => Run(async () =>
    {
        testMusicPlayer.PlayPrevious();
        Assert.Null(testPlayer.CurrentlyPlaying);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayPrevious_Normal() => Run(async () =>
    {
        PrepareWithCachedPlaylist();
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 2));
        testMusicPlayer.PlayPrevious();
        Assert.Equal(2, playerData.CurrentIndex);
        testMusicPlayer.PlayPrevious();
        Assert.Equal(1, playerData.CurrentIndex);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayPrevious_AtStart() => Run(async () =>
    {
        PrepareWithCachedPlaylist();
        testPlayer.Play();
        testMusicPlayer.PlayPrevious();
        Assert.Equal(0, playerData.CurrentIndex);
        testMusicPlayer.PlayPrevious();
        Assert.Equal(0, playerData.CurrentIndex);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayPrevious_RepeatAll() => Run(async () =>
    {
        PrepareWithCachedPlaylist();
        testPlayer.SetRepeatMode(RepeatMode.All);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 0));
        testMusicPlayer.PlayPrevious();
        testMusicPlayer.PlayPrevious();
        Assert.Equal(8, playerData.CurrentIndex);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayPrevious_RepeatAll_OnlyOneSong() => Run(async () =>
    {
        testPlayer.Play(new PlayContext("", [songCached]));
        testPlayer.SetRepeatMode(RepeatMode.All);
        testMusicPlayer.PlayPrevious();
        testMusicPlayer.PlayPrevious();
        Assert.Equal(0, playerData.CurrentIndex);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayPrevious_StartPlayIfPaused() => Run(async () =>
    {
        PrepareWithCachedPlaylist();
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 3));
        testMusicPlayer.Pause();
        testMusicPlayer.PlayPrevious();
        Assert.True(testPlayer.IsPlaying);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayPrevious_IsPlayingStaysTrue() => Run(async () =>
    {
        PrepareWithCachedPlaylist();
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 3));
        testMusicPlayer.PlayPrevious();
        Assert.True(testPlayer.IsPlaying);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayNext_EmptyPlaylist() => Run(async () =>
    {
        testPlayer.PlayNext();
        Assert.Null(testPlayer.CurrentlyPlaying);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayNext_Normal() => Run(async () =>
    {
        PrepareWithCachedPlaylist();
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 2));
        testPlayer.PlayNext();
        Assert.Equal(4, playerData.CurrentIndex);
        testPlayer.PlayNext();
        Assert.Equal(5, playerData.CurrentIndex);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayNext_AtStart() => Run(async () =>
    {
        PrepareWithCachedPlaylist();
        testPlayer.Play();
        testPlayer.PlayNext();
        Assert.Equal(1, playerData.CurrentIndex);
        testPlayer.PlayNext();
        Assert.Equal(2, playerData.CurrentIndex);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayNext_RepeatAll() => Run(async () =>
    {
        PrepareWithCachedPlaylist();
        testPlayer.SetRepeatMode(RepeatMode.All);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 7));
        testPlayer.PlayNext();
        testPlayer.PlayNext();
        Assert.Equal(1, playerData.CurrentIndex);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayNext_RepeatAll_OnlyOneSong() => Run(async () =>
    {
        testPlayer.Play(new PlayContext("", [songCached]));
        testPlayer.SetRepeatMode(RepeatMode.All);
        testPlayer.PlayNext();
        testPlayer.PlayNext();
        Assert.Equal(0, playerData.CurrentIndex);
        Assert.Equal(songCached.Id, testPlayer.CurrentlyPlaying?.Id);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayNext_StartPlayIfPaused() => Run(async () =>
    {
        mockMusicPlayable.Expect(1);
        mockMusicPlayable.Expect(2);
        PrepareWithCachedPlaylist();
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 3));
        testMusicPlayer.Pause();
        testPlayer.PlayNext();
        Assert.False(testPlayer.IsPlaying);
        await mockMusicPlayable.WaitAsync(3.0);
        Assert.True(testPlayer.IsPlaying);
    });

    [Fact]
    public void TestPlayNext_IsPlayingStaysTrue() => Run(async () =>
    {
        mockMusicPlayable.Expect(1);
        mockMusicPlayable.Expect(2);
        PrepareWithCachedPlaylist();
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 3));
        testPlayer.PlayNext();
        Assert.False(testPlayer.IsPlaying);
        await mockMusicPlayable.WaitAsync(2.0);
        Assert.True(testPlayer.IsPlaying);
    });

    [Fact]
    public void TestStop_EmptyPlaylist() => Run(async () =>
    {
        testPlayer.Stop();
        Assert.False(testPlayer.IsPlaying);
        Assert.Null(testPlayer.CurrentlyPlaying);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestStop_Playing() => Run(async () =>
    {
        PrepareWithCachedPlaylist();
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 6));
        testPlayer.Stop();
        Assert.False(testPlayer.IsPlaying);
        Assert.Equal(playlistThreeCached.Playables[0].Id, testPlayer.CurrentlyPlaying?.Id);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestStop_AlreadyStopped() => Run(async () =>
    {
        PrepareWithCachedPlaylist();
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 6));
        testPlayer.Stop();
        testPlayer.Stop();
        Assert.False(testPlayer.IsPlaying);
        Assert.Equal(0, playerData.CurrentIndex);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestTogglePlay_EmptyPlaylist() => Run(async () =>
    {
        testPlayer.TogglePlayPause();
        Assert.False(testPlayer.IsPlaying);
        testPlayer.TogglePlayPause();
        Assert.False(testPlayer.IsPlaying);
        testPlayer.TogglePlayPause();
        Assert.False(testPlayer.IsPlaying);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestTogglePlay_AfterPlay() => Run(async () =>
    {
        mockMusicPlayable.Expect(1);
        PrepareWithCachedPlaylist();
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 6));
        await mockMusicPlayable.WaitAsync(2.0);
        testPlayer.TogglePlayPause();
        Assert.False(testPlayer.IsPlaying);

        mockMusicPlayable.Expect(1);
        testPlayer.TogglePlayPause();
        await mockMusicPlayable.WaitAsync(2.0);
        Assert.True(testPlayer.IsPlaying);

        testPlayer.TogglePlayPause();
        Assert.False(testPlayer.IsPlaying);

        mockMusicPlayable.Expect(1);
        testPlayer.TogglePlayPause();
        await mockMusicPlayable.WaitAsync(2.0);
        Assert.True(testPlayer.IsPlaying);
    });

    [Fact]
    public void TestRemoveFromPlaylist_RemoveNotCurrentSong() => Run(async () =>
    {
        PrepareWithCachedPlaylist();
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 3));
        testPlayer.RemovePlayable(new PlayerIndex(PlayerQueueType.Prev, 2));
        Assert.Equal(3, playerData.CurrentIndex);
        testPlayer.RemovePlayable(new PlayerIndex(PlayerQueueType.Next, 1));
        Assert.Equal(3, playerData.CurrentIndex);
        testPlayer.RemovePlayable(new PlayerIndex(PlayerQueueType.Next, 0));
        Assert.Equal(3, playerData.CurrentIndex);
        testPlayer.RemovePlayable(new PlayerIndex(PlayerQueueType.Prev, 0));
        Assert.Equal(2, playerData.CurrentIndex);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayer_InsertNextInMainQueue_emptyMainQueue() => Run(async () =>
    {
        testPlayer.ClearContextQueue();
        playerData.SetCurrentIndex(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), []);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), []);
        testPlayer.InsertContextQueue([songCached]);
        CheckCurrentlyPlaying(4);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), []);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), []);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayer_InsertNextInMainQueue_emptyMainQueue_userQueuePlaing() => Run(async () =>
    {
        playerData.SetCurrentIndex(0);
        testPlayer.InsertUserQueue([songCached]);
        CheckCurrentlyPlaying(4);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), []);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), []);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), []);
        CheckQueueInfoConsistency();
        testPlayer.InsertContextQueue([songToDownload]);
        CheckCurrentlyPlaying(4);
        Assert.Equal(-1, playerData.CurrentIndex);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), []);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), []);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayer_InsertNextInMainQueue_WithWaitingQueuePlaying_nextEmpty() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(2);
        testPlayer.RemovePlayable(new PlayerIndex(PlayerQueueType.Next, 0));
        testPlayer.RemovePlayable(new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), []);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        testPlayer.InsertContextQueue([songCached]);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayer_InsertNextInMainQueue_noWaitingQueuePlaying_nextEmpty() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(2);
        testPlayer.RemovePlayable(new PlayerIndex(PlayerQueueType.Next, 0));
        testPlayer.RemovePlayable(new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), []);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();
        testPlayer.InsertContextQueue([songCached]);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayer_InsertNextInMainQueue_WithWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(2);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        testPlayer.InsertContextQueue([songCached]);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayer_InsertNextInMainQueue_noWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(2);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();
        testPlayer.InsertContextQueue([songCached]);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayer_AppendNextInMainQueue_WithWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(2);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        testPlayer.AppendContextQueue([songToDownload]);
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4, 0]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayer_AppendNextInMainQueue_noWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(2);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();
        testPlayer.AppendContextQueue([songToDownload]);
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4, 0]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayer_PlayPrev_WithWaitingQueue_noWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(3);
        testMusicPlayer.PlayPrevious();
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();
        testMusicPlayer.PlayPrevious();
        CheckCurrentlyPlaying(1);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();
        testMusicPlayer.PlayPrevious();
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(0);
        testPlayer.SetRepeatMode(RepeatMode.All);
        testMusicPlayer.PlayPrevious();
        CheckCurrentlyPlaying(4);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayer_PlayPrev_WithWaitingQueue_withWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(2);
        testMusicPlayer.PlayPrevious();
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        testMusicPlayer.PlayPrevious();
        CheckCurrentlyPlaying(1);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        testMusicPlayer.PlayPrevious();
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(-1);
        CheckCurrentlyPlaying(5);
        testMusicPlayer.PlayPrevious();
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [0, 1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(-1);
        testPlayer.SetRepeatMode(RepeatMode.All);
        CheckCurrentlyPlaying(5);
        testMusicPlayer.PlayPrevious();
        CheckCurrentlyPlaying(4);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayer_PlayNext_WithWaitingQueue_noWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(2);
        testPlayer.PlayNext();
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(0);
        testPlayer.PlayNext();
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(4);
        testPlayer.PlayNext();
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayer_PlayNext_WithWaitingQueue_withWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(2);
        testPlayer.PlayNext();
        CheckCurrentlyPlaying(6);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(-1);
        CheckCurrentlyPlaying(5);
        testPlayer.PlayNext();
        CheckCurrentlyPlaying(6);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [0, 1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [7, 8]);
        CheckQueueInfoConsistency();
        testPlayer.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testPlayer.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testPlayer.PlayNext();
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(0);
        testPlayer.PlayNext();
        CheckCurrentlyPlaying(6);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [7, 8]);
        CheckQueueInfoConsistency();
        testPlayer.PlayNext();
        CheckCurrentlyPlaying(7);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [8]);
        CheckQueueInfoConsistency();
        testPlayer.PlayNext();
        CheckCurrentlyPlaying(8);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.PlayNext();
        CheckCurrentlyPlaying(1);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testPlayer.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        playerData.SetCurrentIndex(4);
        testPlayer.PlayNext();
        CheckCurrentlyPlaying(8);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetRepeatMode(RepeatMode.All);
        testPlayer.PlayNext();
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testPlayer.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        playerData.SetCurrentIndex(4);
        testPlayer.PlayNext();
        CheckCurrentlyPlaying(8);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();
        testPlayer.SetRepeatMode(RepeatMode.Single);
        testPlayer.PlayNext();
        Assert.False(testPlayer.IsPlaying);
        Assert.False(playerData.IsUserQueuePlaying);
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayer_Play_indexValidation() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(-1);
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();
        testPlayer.Play();
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayer_PlayPlayIndex_prev_noWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(1);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(2);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(2);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Prev, 1));
        CheckCurrentlyPlaying(1);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(4);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Prev, 3));
        CheckCurrentlyPlaying(3);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(4);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayer_PlayPlayIndex_prev_withWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(0);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(2);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(2);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Prev, 1));
        CheckCurrentlyPlaying(1);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(4);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Prev, 4));
        CheckCurrentlyPlaying(4);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(4);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Prev, 3));
        CheckCurrentlyPlaying(3);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(4);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Prev, 0));
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayer_PlayPlayIndex_next_noWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(3);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(4);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(2);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(3);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(2);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 1));
        CheckCurrentlyPlaying(4);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(0);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 3));
        CheckCurrentlyPlaying(4);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(0);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(1);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [5, 6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayer_PlayPlayIndex_next_withWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(3);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(4);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(2);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(3);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(2);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 1));
        CheckCurrentlyPlaying(4);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(-1);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(0);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(-1);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 4));
        CheckCurrentlyPlaying(4);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(-1);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 2));
        CheckCurrentlyPlaying(2);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(0);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.Next, 0));
        CheckCurrentlyPlaying(1);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayer_PlayPlayIndex_wait_noWaitingQueuePlaying() => Run(async () =>
    {
        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(3);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(2);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(2);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.User, 1));
        CheckCurrentlyPlaying(6);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(0);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.User, 3));
        CheckCurrentlyPlaying(8);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(0);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.User, 2));
        CheckCurrentlyPlaying(7);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        playerData.SetCurrentIndex(4);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(5);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [6, 7, 8]);
        CheckQueueInfoConsistency();

        PrepareNoWaitingQueuePlaying();
        testPlayer.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testPlayer.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testPlayer.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        playerData.SetCurrentIndex(4);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(8);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestPlayer_PlayPlayIndex_wait_withWaitingQueuePlaying() => Run(async () =>
    {
        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(3);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(6);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(2);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(6);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(2);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.User, 1));
        CheckCurrentlyPlaying(7);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(4);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.User, 2));
        CheckCurrentlyPlaying(8);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0, 1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(0);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.User, 2));
        CheckCurrentlyPlaying(8);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        testPlayer.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        testPlayer.RemovePlayable(new PlayerIndex(PlayerQueueType.User, 0));
        playerData.SetCurrentIndex(0);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(8);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), [0]);
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), Array.Empty<int>());
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(-1);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.User, 0));
        CheckCurrentlyPlaying(6);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [0, 1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [7, 8]);
        CheckQueueInfoConsistency();

        PrepareWithWaitingQueuePlaying();
        playerData.SetCurrentIndex(-1);
        testPlayer.Play(new PlayerIndex(PlayerQueueType.User, 1));
        CheckCurrentlyPlaying(7);
        CheckQueueItems(testQueueHandler.GetAllPrevQueueItems(), Array.Empty<int>());
        CheckQueueItems(testQueueHandler.GetAllNextQueueItems(), [0, 1, 2, 3, 4]);
        CheckQueueItems(testQueueHandler.GetAllUserQueueItems(), [8]);
        CheckQueueInfoConsistency();
        await Task.CompletedTask;
    });

    [Fact]
    public void TestSongFinishedPlaying_RepeatSingle() => Run(async () =>
    {
        PrepareWithAllSongsCached();
        playerData.SetCurrentIndex(1);
        testPlayer.Play();
        testPlayer.SetRepeatMode(RepeatMode.Single);
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(playlistAllCached.Playables[1].Id, testPlayer.CurrentlyPlaying?.Id);

        backendPlayer.Responder?.DidItemFinishedPlaying();
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(playlistAllCached.Playables[1].Id, testPlayer.CurrentlyPlaying?.Id);

        backendPlayer.Responder?.DidItemFinishedPlaying();
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(playlistAllCached.Playables[1].Id, testPlayer.CurrentlyPlaying?.Id);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestSongFinishedPlaying_RepeatAll() => Run(async () =>
    {
        PrepareWithAllSongsCached();
        playerData.SetCurrentIndex(1);
        testPlayer.Play();
        testPlayer.SetRepeatMode(RepeatMode.All);
        Assert.Equal(playlistAllCached.Playables[1].Id, testPlayer.CurrentlyPlaying?.Id);

        mockMusicPlayable.Expect(1);
        mockMusicPlayable.Expect(1);
        backendPlayer.Responder?.DidItemFinishedPlaying();
        await mockMusicPlayable.WaitAsync(2.0);
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(playlistAllCached.Playables[2].Id, testPlayer.CurrentlyPlaying?.Id);

        mockMusicPlayable.Expect(1);
        mockMusicPlayable.Expect(1);
        backendPlayer.Responder?.DidItemFinishedPlaying();
        await mockMusicPlayable.WaitAsync(2.0);
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(playlistAllCached.Playables[3].Id, testPlayer.CurrentlyPlaying?.Id);

        mockMusicPlayable.Expect(1);
        mockMusicPlayable.Expect(1);
        backendPlayer.Responder?.DidItemFinishedPlaying();
        await mockMusicPlayable.WaitAsync(2.0);
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(playlistAllCached.Playables[0].Id, testPlayer.CurrentlyPlaying?.Id);

        mockMusicPlayable.Expect(1);
        mockMusicPlayable.Expect(1);
        backendPlayer.Responder?.DidItemFinishedPlaying();
        await mockMusicPlayable.WaitAsync(2.0);
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(playlistAllCached.Playables[1].Id, testPlayer.CurrentlyPlaying?.Id);
    });

    [Fact]
    public void TestSongFinishedPlaying_RepeatAll_OneSong() => Run(async () =>
    {
        testPlayer.Play(new PlayContext("", [songCached]));

        testPlayer.SetRepeatMode(RepeatMode.Single);

        mockMusicPlayable.Expect(1);
        mockMusicPlayable.Expect(1);
        backendPlayer.Responder?.DidItemFinishedPlaying();
        await mockMusicPlayable.WaitAsync(2.0);
        Assert.True(testPlayer.IsPlaying);

        mockMusicPlayable.Expect(1);
        mockMusicPlayable.Expect(1);
        backendPlayer.Responder?.DidItemFinishedPlaying();
        await mockMusicPlayable.WaitAsync(2.0);
        Assert.True(testPlayer.IsPlaying);

        testPlayer.SetRepeatMode(RepeatMode.All);

        mockMusicPlayable.Expect(1);
        mockMusicPlayable.Expect(1);
        backendPlayer.Responder?.DidItemFinishedPlaying();
        await mockMusicPlayable.WaitAsync(2.0);
        Assert.True(testPlayer.IsPlaying);

        mockMusicPlayable.Expect(1);
        mockMusicPlayable.Expect(1);
        backendPlayer.Responder?.DidItemFinishedPlaying();
        await mockMusicPlayable.WaitAsync(2.0);
        Assert.True(testPlayer.IsPlaying);

        testPlayer.SetRepeatMode(RepeatMode.Off);

        backendPlayer.Responder?.DidItemFinishedPlaying();
        Assert.False(testPlayer.IsPlaying);
    });

    [Fact]
    public void TestSongFinishedPlaying_RepeatOff() => Run(async () =>
    {
        PrepareWithAllSongsCached();
        playerData.SetCurrentIndex(1);
        testPlayer.Play();
        testPlayer.SetRepeatMode(RepeatMode.Off);
        Assert.Equal(playlistAllCached.Playables[1].Id, testPlayer.CurrentlyPlaying?.Id);
        backendPlayer.Responder?.DidItemFinishedPlaying();
        Assert.Equal(playlistAllCached.Playables[2].Id, testPlayer.CurrentlyPlaying?.Id);

        backendPlayer.Responder?.DidItemFinishedPlaying();
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(playlistAllCached.Playables[3].Id, testPlayer.CurrentlyPlaying?.Id);

        backendPlayer.Responder?.DidItemFinishedPlaying();
        Assert.False(testPlayer.IsPlaying);
        Assert.Equal(playlistAllCached.Playables[0].Id, testPlayer.CurrentlyPlaying?.Id);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestSongFinishedPlaying_RepeatSingleOffMix() => Run(async () =>
    {
        PrepareWithAllSongsCached();
        playerData.SetCurrentIndex(1);
        testPlayer.Play();
        testPlayer.SetRepeatMode(RepeatMode.Single);
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(playlistAllCached.Playables[1].Id, testPlayer.CurrentlyPlaying?.Id);

        backendPlayer.Responder?.DidItemFinishedPlaying();
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(playlistAllCached.Playables[1].Id, testPlayer.CurrentlyPlaying?.Id);

        testPlayer.SetRepeatMode(RepeatMode.Off);
        backendPlayer.Responder?.DidItemFinishedPlaying();
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(playlistAllCached.Playables[2].Id, testPlayer.CurrentlyPlaying?.Id);

        testPlayer.SetRepeatMode(RepeatMode.Single);
        backendPlayer.Responder?.DidItemFinishedPlaying();
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(playlistAllCached.Playables[2].Id, testPlayer.CurrentlyPlaying?.Id);

        backendPlayer.Responder?.DidItemFinishedPlaying();
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(playlistAllCached.Playables[2].Id, testPlayer.CurrentlyPlaying?.Id);

        testPlayer.SetRepeatMode(RepeatMode.Off);
        backendPlayer.Responder?.DidItemFinishedPlaying();
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(playlistAllCached.Playables[3].Id, testPlayer.CurrentlyPlaying?.Id);

        backendPlayer.Responder?.DidItemFinishedPlaying();
        Assert.False(testPlayer.IsPlaying); // not playing
        Assert.Equal(playlistAllCached.Playables[0].Id, testPlayer.CurrentlyPlaying?.Id);
        await Task.CompletedTask;
    });

    [Fact]
    public void TestSongFinishedPlaying_RepeatSingleAllMix() => Run(async () =>
    {
        PrepareWithAllSongsCached();
        playerData.SetCurrentIndex(1);
        testPlayer.Play();
        testPlayer.SetRepeatMode(RepeatMode.Single);
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(playlistAllCached.Playables[1].Id, testPlayer.CurrentlyPlaying?.Id);

        backendPlayer.Responder?.DidItemFinishedPlaying();
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(playlistAllCached.Playables[1].Id, testPlayer.CurrentlyPlaying?.Id);

        testPlayer.SetRepeatMode(RepeatMode.All);
        backendPlayer.Responder?.DidItemFinishedPlaying();
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(playlistAllCached.Playables[2].Id, testPlayer.CurrentlyPlaying?.Id);

        backendPlayer.Responder?.DidItemFinishedPlaying();
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(playlistAllCached.Playables[3].Id, testPlayer.CurrentlyPlaying?.Id);

        backendPlayer.Responder?.DidItemFinishedPlaying();
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(playlistAllCached.Playables[0].Id, testPlayer.CurrentlyPlaying?.Id);

        testPlayer.SetRepeatMode(RepeatMode.Single);
        backendPlayer.Responder?.DidItemFinishedPlaying();
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(playlistAllCached.Playables[0].Id, testPlayer.CurrentlyPlaying?.Id);

        backendPlayer.Responder?.DidItemFinishedPlaying();
        Assert.True(testPlayer.IsPlaying);
        Assert.Equal(playlistAllCached.Playables[0].Id, testPlayer.CurrentlyPlaying?.Id);
        await Task.CompletedTask;
    });
}
