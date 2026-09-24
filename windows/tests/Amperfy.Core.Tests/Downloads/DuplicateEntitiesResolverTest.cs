using Amperfy.Core.Downloads;
using Amperfy.Core.Sync;

namespace Amperfy.Core.Tests.Downloads;

[Collection(DownloadTestCollection.Name)]
public class DuplicateEntitiesResolverTest : IDisposable
{
    private readonly Helper.TestStorage _t = new();
    private LibraryStorage Library => _t.Library;
    private Account Account => _t.Account;

    public void Dispose() => _t.Dispose();

    private Song CreateSong(string id, Album? album = null)
    {
        var s = Library.CreateSong(Account);
        s.Id = id;
        s.Title = $"Song {id}";
        s.Album = album;
        return s;
    }

    [Fact]
    public void FindDuplicatesIgnoresEmptyIdsAndOtherAccounts()
    {
        var other = Library.GetAccount(AccountInfo.Create("https://other.example", "u", BackendApiType.Subsonic));
        CreateSong("a");
        CreateSong("a");
        CreateSong("a");
        CreateSong("b");
        CreateSong("");
        CreateSong("");
        var otherSong1 = Library.CreateSong(other);
        otherSong1.Id = "b";
        Library.SaveContext();

        var duplicates = Library.FindDuplicates(DuplicateEntityType.Song, Account);
        var d = Assert.Single(duplicates);
        Assert.Equal(new LibraryDuplicateInfo("a", 3), d);
    }

    [Fact]
    public void ResolveSongsDuplicatesPassesOwnership()
    {
        var album = Library.CreateAlbum(Account);
        album.Id = "al";
        var lead = CreateSong("s1", album);
        Library.SaveContext();
        var duplicate = CreateSong("s1", album);
        var playlist = Library.CreatePlaylist(Account);
        playlist.Name = "P";
        Library.SaveContext();
        playlist.Append(duplicate);
        var scrobble = Library.CreateScrobbleEntry(Account);
        scrobble.Playable = duplicate;
        var download = Library.CreateDownload(Account, duplicate.UniqueId());
        download.Element = duplicate;
        var embedded = Library.CreateEmbeddedArtwork(Account);
        embedded.Owner = duplicate;
        embedded.RelFilePath = "x.png";
        Library.SaveContext();

        Library.ResolveSongsDuplicates(Account, Library.FindDuplicates(DuplicateEntityType.Song, Account));
        Library.SaveContext();

        var remaining = Assert.Single(Library.GetSongs(Account));
        Assert.Same(lead, remaining);
        Assert.Same(lead, Assert.Single(playlist.Playables));
        Assert.Same(lead, scrobble.Playable);
        Assert.Same(download, lead.Download);
        Assert.Same(embedded, lead.EmbeddedArtwork);
        Assert.Single(Library.GetAllDownloads());
        Assert.Single(Library.GetAllEmbeddedArtworks());
        Assert.Empty(Library.FindDuplicates(DuplicateEntityType.Song, Account));
    }

    [Fact]
    public void ResolveSongsDuplicatesDeletesDownloadIfLeadHasOne()
    {
        var lead = CreateSong("s1");
        Library.SaveContext();
        var duplicate = CreateSong("s1");
        Library.SaveContext();
        var leadDownload = Library.CreateDownload(Account, "lead");
        leadDownload.Element = lead;
        var dupDownload = Library.CreateDownload(Account, "dup");
        dupDownload.Element = duplicate;
        Library.SaveContext();

        Library.ResolveSongsDuplicates(Account, Library.FindDuplicates(DuplicateEntityType.Song, Account));
        Library.SaveContext();

        Assert.Same(leadDownload, Assert.Single(Library.GetAllDownloads()));
    }

    [Fact]
    public void ResolveAlbumAndArtistDuplicates()
    {
        var artist1 = Library.CreateArtist(Account);
        artist1.Id = "ar";
        artist1.Name = "Artist";
        Library.SaveContext();
        var artist2 = Library.CreateArtist(Account);
        artist2.Id = "ar";
        artist2.Name = "Artist";
        var album1 = Library.CreateAlbum(Account);
        album1.Id = "al";
        album1.Artist = artist1;
        Library.SaveContext();
        var album2 = Library.CreateAlbum(Account);
        album2.Id = "al";
        album2.Artist = artist2;
        CreateSong("s1", album1).Artist = artist1;
        CreateSong("s2", album2).Artist = artist2;
        Library.SaveContext();

        Library.ResolveArtistsDuplicates(Account, Library.FindDuplicates(DuplicateEntityType.Artist, Account));
        Library.SaveContext();
        Library.ResolveAlbumsDuplicates(Account, Library.FindDuplicates(DuplicateEntityType.Album, Account));
        Library.SaveContext();

        Assert.Same(artist1, Assert.Single(Library.GetArtists(Account)));
        Assert.Same(album1, Assert.Single(Library.GetAlbums(Account)));
        Assert.Equal(["s1", "s2"], album1.Songs.Select(s => s.Id).Order());
        Assert.All(Library.GetSongs(Account), s => Assert.Same(artist1, s.Artist));
        Assert.Same(artist1, album1.Artist);
    }

    [Fact]
    public void ResolveGenreDuplicatesByName()
    {
        var genre1 = Library.CreateGenre(Account);
        genre1.Name = "Rock";
        Library.SaveContext();
        var genre2 = Library.CreateGenre(Account);
        genre2.Name = "Rock";
        CreateSong("s1").Genre = genre2;
        Library.SaveContext();

        Assert.Empty(Library.FindDuplicates(DuplicateEntityType.GenreById, Account)); // Subsonic genres have no ids
        Library.ResolveGenresDuplicates(Account, Library.FindDuplicates(DuplicateEntityType.GenreByName, Account), byName: true);
        Library.SaveContext();

        Assert.Same(genre1, Assert.Single(Library.GetGenres(Account)));
        Assert.Same(genre1, Library.GetSong(Account, "s1")!.Genre);
    }

    [Fact]
    public void ResolvePodcastPlaylistAndRadioDuplicates()
    {
        var podcast1 = Library.CreatePodcast(Account);
        podcast1.Id = "p";
        Library.SaveContext();
        var podcast2 = Library.CreatePodcast(Account);
        podcast2.Id = "p";
        var episode = Library.CreatePodcastEpisode(Account);
        episode.Id = "e";
        episode.Podcast = podcast2;
        var playlist1 = Library.CreatePlaylist(Account);
        playlist1.Id = "pl";
        Library.SaveContext();
        var playlist2 = Library.CreatePlaylist(Account);
        playlist2.Id = "pl";
        var radio1 = Library.CreateRadio(Account);
        radio1.Id = "r";
        Library.SaveContext();
        var radio2 = Library.CreateRadio(Account);
        radio2.Id = "r";
        Library.SaveContext();

        var resolver = new DuplicateEntitiesResolver(Account, Library);
        SingleThreadSynchronizationContext.Run(async () =>
        {
            resolver.Start();
            Assert.True(resolver.IsActive);
            await resolver.RunningTask;
        });

        Assert.False(resolver.IsActive);
        Assert.Same(podcast1, Assert.Single(Library.GetPodcasts(Account)));
        Assert.Same(podcast1, episode.Podcast);
        Assert.Same(playlist1, Assert.Single(Library.GetPlaylists(Account)));
        Assert.Same(radio1, Assert.Single(Library.GetRadios(Account)));
    }

    [Fact]
    public void ResolverRunsAllSteps()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            CreateSong("s1");
            Library.SaveContext();
            CreateSong("s1");
            var album1 = Library.CreateAlbum(Account);
            album1.Id = "al";
            Library.SaveContext();
            var album2 = Library.CreateAlbum(Account);
            album2.Id = "al";
            Library.SaveContext();

            var resolver = new DuplicateEntitiesResolver(Account, Library);
            resolver.Start();
            await resolver.RunningTask;

            Assert.Single(Library.GetSongs(Account));
            Assert.Single(Library.GetAlbums(Account));
        });
    }
}
