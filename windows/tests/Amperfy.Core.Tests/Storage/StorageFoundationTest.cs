using Amperfy.Core.Tests.Helper;
using Microsoft.EntityFrameworkCore;

namespace Amperfy.Core.Tests.Storage;

public class StorageFoundationTest : IDisposable
{
    private readonly TestStorage _t = new();
    private LibraryStorage Library => _t.Library;
    private Account Account => _t.Account;

    public void Dispose() => _t.Dispose();

    private Song CreateSong(string id, string title, Album? album = null)
    {
        var s = Library.CreateSong(Account);
        s.Id = id;
        s.Title = title;
        s.Album = album;
        s.Size = 100;
        return s;
    }

    [Fact]
    public void AccountIsCreatedOnce()
    {
        var again = Library.GetAccount(AccountInfo.Create("https://test.example", "testuser", BackendApiType.Subsonic));
        Assert.Same(Account, again);
        Assert.Single(Library.GetAllAccounts());
        Assert.Equal(16, Account.ServerHash.Length);
    }

    [Fact]
    public void MovingSongToAnotherAlbum_UpdatesCountsOfBothAlbums()
    {
        var album1 = Library.CreateAlbum(Account);
        album1.Id = "al1";
        var album2 = Library.CreateAlbum(Account);
        album2.Id = "al2";
        var song = CreateSong("s1", "Song", album1);
        CreateSong("s2", "Other", album1);
        Library.SaveContext();
        Assert.Equal(2, album1.SongCountRaw);
        Assert.Equal(0, album2.SongCountRaw);

        // reads the original album (foreign key original value) during save
        song.Album = album2;
        Library.SaveContext();
        Assert.False(Library.Context.ChangeTracker.HasChanges());
        Assert.Equal(1, album1.SongCountRaw);
        Assert.Equal(1, album2.SongCountRaw);
        Assert.Same(album2, Library.Context.Songs.Single(s => s.Id == "s1").Album);
    }

    [Fact]
    public void ChangesAreTrackedWithoutSnapshotComparison()
    {
        var song = CreateSong("s1", "Song");
        Library.SaveContext();
        Library.Context.ChangeTracker.AutoDetectChangesEnabled = false;
        song.Title = "Renamed";
        Assert.Equal(EntityState.Modified, Library.Context.Entry(song).State);
        Library.Context.ChangeTracker.AutoDetectChangesEnabled = true;
    }

    [Fact]
    public void SongAlbumRelationshipAndCounts()
    {
        var album = Library.CreateAlbum(Account);
        album.Id = "al1";
        album.Name = "Album One";
        CreateSong("s1", "Second", album).Track = 2;
        CreateSong("s2", "First", album).Track = 1;
        Library.SaveContext();

        Assert.Equal(2, album.SongCount);
        Assert.Equal(["s2", "s1"], album.Songs.Select(s => s.Id));
        Assert.Equal("A", album.AlphabeticSectionInitial);
        Assert.Equal(2, Library.GetSongCount(Account));
        Assert.Same(album, Library.GetAlbum(Account, "al1"));
    }

    [Fact]
    public void LazyLoadingWorksAfterReload()
    {
        var album = Library.CreateAlbum(Account);
        album.Id = "al1";
        CreateSong("s1", "Song", album);
        Library.SaveContext();
        Library.Context.ChangeTracker.Clear();

        var loadedAlbum = Library.GetAlbum(Account, "al1")!;
        Assert.Single(loadedAlbum.Songs);
        Assert.Equal("Song", loadedAlbum.Songs[0].Title);
        Assert.Same(loadedAlbum, loadedAlbum.Songs[0].Album);
        Assert.NotNull(loadedAlbum.Account);
    }

    [Fact]
    public void DeletingSongUpdatesAlbumCount()
    {
        var album = Library.CreateAlbum(Account);
        var s1 = CreateSong("s1", "a", album);
        CreateSong("s2", "b", album);
        Library.SaveContext();
        Assert.Equal(2, album.SongCount);
        Library.DeleteSong(s1);
        Library.SaveContext();
        Assert.Equal(1, album.SongCount);
    }

    [Fact]
    public void MovingSongBetweenAlbumsUpdatesBothCounts()
    {
        var a1 = Library.CreateAlbum(Account);
        var a2 = Library.CreateAlbum(Account);
        var s = CreateSong("s1", "a", a1);
        Library.SaveContext();
        Library.Context.ChangeTracker.Clear();
        var la1 = Library.Context.Albums.Find(a1.Pk)!;
        var la2 = Library.Context.Albums.Find(a2.Pk)!;
        var ls = Library.Context.Songs.Find(s.Pk)!;
        Assert.Equal(1, la1.SongCount);
        ls.Album = la2;
        Library.SaveContext();
        Assert.Equal(0, la1.SongCountRaw);
        Assert.Equal(1, la2.SongCountRaw);
    }

    [Fact]
    public void PlaylistAppendInsertMoveRemove()
    {
        var songs = Enumerable.Range(0, 5).Select(i => CreateSong($"s{i}", $"Song {i}")).ToList<AbstractPlayable>();
        var playlist = Library.CreatePlaylist(Account);
        playlist.Name = "Test";
        playlist.Append(songs.Take(3).ToList());
        Library.SaveContext();
        Assert.Equal(3, playlist.SongCount);
        Assert.Equal(["s0", "s1", "s2"], playlist.Playables.Select(p => p.Id));

        playlist.Insert([songs[3]], 1);
        Assert.Equal(["s0", "s3", "s1", "s2"], playlist.Playables.Select(p => p.Id));

        playlist.MovePlaylistItem(0, 3);
        Assert.Equal(["s3", "s1", "s2", "s0"], playlist.Playables.Select(p => p.Id));

        playlist.Remove(1);
        Assert.Equal(["s3", "s2", "s0"], playlist.Playables.Select(p => p.Id));
        Library.SaveContext();

        // order survives a reload
        Library.Context.ChangeTracker.Clear();
        var reloaded = Library.Context.Playlists.Find(playlist.Pk)!;
        Assert.Equal(["s3", "s2", "s0"], reloaded.Playables.Select(p => p.Id));
        Assert.Equal(3, reloaded.SongCount);
        Assert.Equal(3, Library.Context.PlaylistItems.Count(i => i.PlaylistPk == playlist.Pk));
    }

    [Fact]
    public void PlaylistInsertWithoutSpaceReassignsOrder()
    {
        var songs = Enumerable.Range(0, 40).Select(i => CreateSong($"s{i}", $"Song {i}")).ToList<AbstractPlayable>();
        var playlist = Library.CreatePlaylist(Account);
        playlist.Append(songs.Take(2).ToList());
        // insert many items between the two -> no space -> reassign
        playlist.Insert(songs.Skip(2).ToList(), 1);
        Assert.Equal(40, playlist.Playables.Count);
        Assert.Equal("s0", playlist.Playables[0].Id);
        Assert.Equal("s2", playlist.Playables[1].Id);
        Assert.Equal("s1", playlist.Playables[^1].Id);
        var orders = playlist.Items.Select(i => i.Order).ToList();
        Assert.Equal(orders.OrderBy(o => o), orders);
        Assert.Equal(orders.Count, orders.Distinct().Count());
    }

    [Fact]
    public void RemoveAllItemsDeletesItems()
    {
        var playlist = Library.CreatePlaylist(Account);
        playlist.Append([CreateSong("s1", "a"), CreateSong("s2", "b")]);
        Library.SaveContext();
        playlist.RemoveAllItems();
        Library.SaveContext();
        Assert.Equal(0, Library.Context.PlaylistItems.Count());
        Assert.Equal(0, playlist.SongCount);
    }

    [Fact]
    public void DeletingSongRemovesPlaylistItems()
    {
        var playlist = Library.CreatePlaylist(Account);
        var s1 = CreateSong("s1", "a");
        playlist.Append([s1, CreateSong("s2", "b")]);
        Library.SaveContext();
        Library.DeleteSong(s1);
        Library.SaveContext();
        Assert.Single(playlist.Playables);
        Assert.Equal(1, playlist.SongCount);
    }

    [Fact]
    public void PlayerStateHasQueuesAndQueuesAreHiddenFromUserPlaylists()
    {
        var state = Library.GetPlayerState();
        Assert.NotNull(state.ContextPlaylist);
        Assert.NotNull(state.UserQueuePlaylist);
        var user = Library.CreatePlaylist(Account);
        user.Name = "Mine";
        Library.SaveContext();
        Assert.Single(Library.GetPlaylists(Account));
        Assert.Same(state, Library.GetPlayerState());
    }

    [Fact]
    public void PrefetchLoadsEntitiesById()
    {
        var album = Library.CreateAlbum(Account);
        album.Id = "al1";
        CreateSong("s1", "a", album);
        var artist = Library.CreateArtist(Account);
        artist.Name = "Local Artist";
        Library.SaveContext();
        var ids = new PrefetchIdContainer();
        ids.AlbumIDs.Add("al1");
        ids.SongIDs.Add("s1");
        ids.SongIDs.Add("missing");
        ids.LocalArtistNames.Add("Local Artist");
        var elements = Library.GetElements(Account, ids);
        Assert.Same(album, elements.PrefetchedAlbumDict["al1"]);
        Assert.Single(elements.PrefetchedSongDict);
        Assert.Same(artist, elements.PrefetchedLocalArtistDict["Local Artist"]);
    }

    [Fact]
    public void AvailableSongsFilter()
    {
        var album = Library.CreateAlbum(Account);
        var available = CreateSong("s1", "a", album);
        var deleted = CreateSong("s2", "b", album);
        deleted.Size = 0;
        var cached = CreateSong("s3", "c");
        cached.Size = 0;
        cached.RelFilePath = "x/y.mp3";
        Library.SaveContext();
        var ids = Library.AvailableSongs().Select(s => s.Id).OrderBy(s => s).ToList();
        Assert.Equal(["s1", "s3"], ids);
        Assert.True(available.IsAvailableToUser());
        Assert.False(deleted.IsAvailableToUser());
        Assert.True(cached.IsAvailableToUser());
    }

    [Fact]
    public void SearchIsCaseInsensitive()
    {
        var a = Library.CreateArtist(Account);
        a.Id = "1";
        a.Name = "Björk";
        var b = Library.CreateArtist(Account);
        b.Id = "2";
        b.Name = "ABBA";
        Library.SaveContext();
        Assert.Single(Library.SearchArtists(Account, "abb", false, ArtistCategoryFilter.All));
        Assert.Equal(["ABBA", "Björk"], Library.SearchArtists(Account, "", false, ArtistCategoryFilter.All).Select(x => x.Name));
    }

    [Fact]
    public void SectionInitial()
    {
        Assert.Equal("A", "abba".SectionInitial());
        Assert.Equal("E", "Élan".SectionInitial());
        Assert.Equal("#", "1999".SectionInitial());
        Assert.Equal("&", "日本".SectionInitial());
        Assert.Equal("?", "".SectionInitial());
        Assert.Equal("?", "(test)".SectionInitial());
    }

    [Fact]
    public void CleanStorageOfAccount()
    {
        CreateSong("s1", "a");
        var p = Library.CreatePlaylist(Account);
        p.Id = "p1";
        Library.SaveContext();
        Library.CleanStorageOfObsoleteAccountEntries(Account);
        Assert.Equal(0, Library.Context.Songs.Count());
        Assert.Equal(0, Library.Context.Playlists.Count());
        Assert.Single(Library.GetAllAccounts());
    }
}
