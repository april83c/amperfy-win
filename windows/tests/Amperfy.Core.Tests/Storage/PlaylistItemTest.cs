using Amperfy.Core.Tests.Helper;
using static Amperfy.Core.Tests.Player.TestUtil;

namespace Amperfy.Core.Tests.Storage;

/// Port of AmperfyKitTests/Cases/Storage/ManagedObjects/PlaylistItemTest.swift
public class PlaylistItemTest
{
    private readonly CoreDataHelper cdHelper;
    private readonly LibraryStorage library;
    private readonly Account account;

    public PlaylistItemTest()
    {
        cdHelper = new CoreDataHelper();
        library = cdHelper.CreateSeededStorage();
        account = library.GetAccount(TestAccountInfo.Create1());
    }

    [Fact]
    public void TestCreation()
    {
        var song1 = NN(library.GetSong(account, cdHelper.Seeder.Songs[0].Id));
        var item = library.CreatePlaylistItem(song1);
        Assert.Equal(TestAccountInfo.Test1ServerHash, item.Account?.ServerHash);
        Assert.Equal(TestAccountInfo.Test1UserHash, item.Account?.UserHash);
        Assert.Equal(0, item.Order);

        Assert.Equal(song1.Id, item.Playable!.Id);
        var playlist = NN(library.GetPlaylist(account, cdHelper.Seeder.Playlists[0].Id));
        var itemOrder = playlist.Playables.Count;
        var lastOrder = playlist.Items[itemOrder - 1].Order;
        // Swift: `item.playlist = playlist` appends the item to the CoreData ordered relationship
        playlist.Add(item);
        Assert.Equal(playlist.Id, item.Playlist!.Id);
        // Swift assigned `order = itemOrder` (5). CoreData kept the item at the end of the ordered relationship
        // regardless of its order value; the C# model orders by PlaylistItem.Order, so an order after the last
        // item is used to keep the item at index `itemOrder`.
        var newOrder = lastOrder + 1;
        item.Order = newOrder;
        Assert.Equal(newOrder, item.Order);
        library.SaveContext();

        var playlistFetched = NN(library.GetPlaylist(account, cdHelper.Seeder.Playlists[0].Id));
        Assert.Equal(song1.Id, playlistFetched.Items[itemOrder].Playable!.Id);
        Assert.Equal(playlistFetched.Id, playlistFetched.Items[itemOrder].Playlist!.Id);
        Assert.Equal(newOrder, playlistFetched.Items[itemOrder].Order);
        Assert.Equal(TestAccountInfo.Test1ServerHash, playlistFetched.Items[itemOrder].Account?.ServerHash);
        Assert.Equal(TestAccountInfo.Test1UserHash, playlistFetched.Items[itemOrder].Account?.UserHash);
    }

    [Fact]
    public void TestOrphanDetectionDeletedPlaylist()
    {
        var playlist = NN(library.GetPlaylist(account, cdHelper.Seeder.Playlists[0].Id));
        var playlistItemCount = playlist.SongCount;
        Assert.True(playlistItemCount > 0);
        var orphans = library.GetAllPlaylistItemOrphans();
        Assert.Empty(orphans);

        library.DeletePlaylist(playlist);
        library.SaveContext(); // EF queries only see saved data (CoreData fetches include pending changes)
        orphans = library.GetAllPlaylistItemOrphans();
        // Delete Rule should delete the orphans
        Assert.Empty(orphans);
        Assert.DoesNotContain(library.GetAllPlaylistItems(), i => i.PlaylistPk == playlist.Pk);
    }

    [Fact]
    public void TestOrphanDetectionDeletedSong()
    {
        var playlist = NN(library.GetPlaylist(account, cdHelper.Seeder.Playlists[0].Id));
        var playlistItemCount = playlist.SongCount;
        Assert.True(playlistItemCount > 0);
        var orphans = library.GetAllPlaylistItemOrphans();
        Assert.Empty(orphans);

        library.DeleteSong(playlist.Playables[0].AsSong!);
        library.SaveContext(); // EF queries only see saved data (CoreData fetches include pending changes)
        orphans = library.GetAllPlaylistItemOrphans();
        // Delete Rule should delete the orphans
        Assert.Empty(orphans);
    }
}
