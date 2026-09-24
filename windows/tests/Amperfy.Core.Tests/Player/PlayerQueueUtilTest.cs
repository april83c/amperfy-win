using Amperfy.Core.Player;
using Amperfy.Core.Tests.Helper;

namespace Amperfy.Core.Tests.Player;

public class PlayerQueueUtilTest
{
    [Fact]
    public void ContextQueueSongsForPlaylist_CombinesPrevCurrentNextSongsInOrder()
    {
        using var storage = new TestStorage();
        var library = storage.Library;
        var s1 = library.CreateSong(storage.Account);
        var s2 = library.CreateSong(storage.Account);
        var s3 = library.CreateSong(storage.Account);
        var radio = library.CreateRadio(storage.Account);
        var episode = library.CreatePodcastEpisode(storage.Account);
        library.SaveContext();

        var songs = PlayerQueueUtil.ContextQueueSongsForPlaylist([s1, radio], s2, [episode, s3]);

        Assert.Equal([s1, s2, s3], songs);
    }

    [Fact]
    public void ContextQueueSongsForPlaylist_CurrentRadioIsSkipped()
    {
        using var storage = new TestStorage();
        var library = storage.Library;
        var s1 = library.CreateSong(storage.Account);
        var radio = library.CreateRadio(storage.Account);
        library.SaveContext();

        Assert.Equal([s1], PlayerQueueUtil.ContextQueueSongsForPlaylist([], radio, [s1]));
        Assert.Empty(PlayerQueueUtil.ContextQueueSongsForPlaylist([], null, []));
    }

    [Fact]
    public void ContextQueueSongsForPlaylist_SongsOfDifferentAccounts_ReturnsEmpty()
    {
        using var storage = new TestStorage();
        var library = storage.Library;
        var other = library.GetAccount(AccountInfo.Create("https://other.example", "otheruser", BackendApiType.Subsonic));
        var s1 = library.CreateSong(storage.Account);
        var s2 = library.CreateSong(other);
        library.SaveContext();

        Assert.Empty(PlayerQueueUtil.ContextQueueSongsForPlaylist([s1], null, [s2]));
    }
}
