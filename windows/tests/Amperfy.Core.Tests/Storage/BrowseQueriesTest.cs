using Amperfy.Core.Tests.Helper;

namespace Amperfy.Core.Tests.Storage;

/// Tests of the library browsing queries (LibraryStorage.Browse.cs).
public class BrowseQueriesTest : IDisposable
{
    private readonly TestStorage _storage = new();
    private LibraryStorage Library => _storage.Library;
    private Account Account => _storage.Account;

    public void Dispose() => _storage.Dispose();

    private Artist CreateArtist(string id, string name, int rating = 0, long duration = 0)
    {
        var artist = Library.CreateArtist(Account);
        artist.Id = id;
        artist.Name = name;
        artist.Rating = rating;
        artist.DurationRaw = duration;
        return artist;
    }

    private Album CreateAlbum(string id, string name, Artist? artist, int year = 0, int rating = 0)
    {
        var album = Library.CreateAlbum(Account);
        album.Id = id;
        album.Name = name;
        album.Artist = artist;
        album.Year = year;
        album.Rating = rating;
        return album;
    }

    private Song CreateSong(string id, string title, Album? album, Artist? artist, bool cached = false, int duration = 0)
    {
        var song = Library.CreateSong(Account);
        song.Id = id;
        song.Title = title;
        song.Album = album;
        song.Artist = artist;
        song.Size = 100;
        song.RemoteDuration = duration;
        if (cached) song.RelFilePath = "cached/" + id;
        return song;
    }

    [Fact]
    public void SortArtistsByName_IsCaseInsensitiveAndGroupedBySection()
    {
        CreateArtist("1", "beta");
        CreateArtist("2", "Alpha");
        CreateArtist("3", "alpaca");
        CreateArtist("4", "3 Doors");
        Library.SaveContext();
        var names = LibraryStorage.SortArtists(Library.QueryArtists(Account, "", false, ArtistCategoryFilter.All), ArtistElementSortType.Name)
            .Select(a => a.NameRaw).ToList();
        Assert.Equal(["3 Doors", "alpaca", "Alpha", "beta"], names);
    }

    [Fact]
    public void SortArtistsByRatingAndDuration()
    {
        CreateArtist("1", "A", rating: 1, duration: 300);
        CreateArtist("2", "B", rating: 5, duration: 100);
        CreateArtist("3", "C", rating: 3, duration: 200);
        Library.SaveContext();
        var q = Library.QueryArtists(Account, "", false, ArtistCategoryFilter.All);
        Assert.Equal(["B", "C", "A"], LibraryStorage.SortArtists(q, ArtistElementSortType.Rating).Select(a => a.NameRaw).ToList());
        Assert.Equal(["B", "C", "A"], LibraryStorage.SortArtists(q, ArtistElementSortType.Duration).Select(a => a.NameRaw).ToList());
    }

    [Fact]
    public void SortAlbums_ByYearArtistAndNewest()
    {
        var zed = CreateArtist("z", "Zed");
        var abba = CreateArtist("a", "Abba");
        var a1 = CreateAlbum("1", "First", zed, 1990);
        var a2 = CreateAlbum("2", "Second", abba, 2010);
        var a3 = CreateAlbum("3", "Third", abba, 2000);
        a3.NewestIndex = 1;
        a1.NewestIndex = 2;
        Library.SaveContext();
        var q = Library.QueryAlbums(Account, "", false, DisplayCategoryFilter.All);
        Assert.Equal(["Second", "Third", "First"], LibraryStorage.SortAlbums(q, AlbumElementSortType.Year).Select(a => a.NameRaw).ToList());
        Assert.Equal(["Second", "Third", "First"], LibraryStorage.SortAlbums(q, AlbumElementSortType.Artist).Select(a => a.NameRaw).ToList());
        var newest = LibraryStorage.SortAlbums(Library.QueryAlbums(Account, "", false, DisplayCategoryFilter.Newest), AlbumElementSortType.Newest);
        Assert.Equal(["Third", "First"], newest.Select(a => a.NameRaw).ToList());
        _ = a2;
    }

    [Fact]
    public void SortSongs_ByDurationAndPaging()
    {
        var album = CreateAlbum("al", "Album", null);
        for (var i = 0; i < 25; i++) CreateSong($"s{i:00}", $"Song {i:00}", album, null, duration: 100 - i);
        Library.SaveContext();
        var q = LibraryStorage.SortSongs(Library.QuerySongs(Account, "", false, DisplayCategoryFilter.All), SongElementSortType.Duration);
        Assert.Equal(25, q.Count());
        var page = q.Skip(10).Take(10).ToList();
        Assert.Equal(10, page.Count);
        Assert.Equal("Song 14", page[0].Title);
    }

    [Fact]
    public void SectionInitialsAndJumpIndex()
    {
        CreateArtist("1", "Beta");
        CreateArtist("2", "Alpha");
        CreateArtist("3", "Bravo");
        CreateArtist("4", "Delta");
        Library.SaveContext();
        var q = Library.QueryArtists(Account, "", false, ArtistCategoryFilter.All);
        Assert.Equal(["A", "B", "D"], LibraryStorage.GetSectionInitials(q));
        Assert.Equal(0, LibraryStorage.CountBeforeSectionInitial(q, "A"));
        Assert.Equal(1, LibraryStorage.CountBeforeSectionInitial(q, "B"));
        Assert.Equal(3, LibraryStorage.CountBeforeSectionInitial(q, "D"));
    }

    [Fact]
    public void ArtistAlbumsAndSongs()
    {
        var artist = CreateArtist("a1", "Artist");
        var other = CreateArtist("a2", "Other");
        var own = CreateAlbum("al1", "Own", artist, 2001);
        var compilation = CreateAlbum("al2", "Compilation", other, 1999);
        CreateSong("s1", "Track A", own, other);
        CreateSong("s2", "Track B", compilation, artist, cached: true);
        CreateSong("s3", "Track C", compilation, other);
        Library.SaveContext();

        Assert.Equal(["Compilation", "Own"], Library.QueryArtistAlbums(artist).Select(a => a.NameRaw).ToList());
        Assert.Equal(["Compilation"], Library.QueryArtistAlbums(artist, onlyCached: true).Select(a => a.NameRaw).ToList());
        Assert.Equal(["Track B"], Library.QueryArtistSongs(artist, ArtistCategoryFilter.All).Select(s => s.TitleRaw).ToList());
        Assert.Equal(["Track A", "Track B"], Library.QueryArtistSongs(artist, ArtistCategoryFilter.AlbumArtists).Select(s => s.TitleRaw).ToList());
        Assert.Equal(["Track B"], Library.QueryArtistSongs(artist, ArtistCategoryFilter.AlbumArtists, "b").Select(s => s.TitleRaw).ToList());
    }

    [Fact]
    public void GenreQueries()
    {
        var genre = Library.CreateGenre(Account);
        genre.Id = "g";
        genre.Name = "Rock";
        var artist = CreateArtist("a1", "Artist");
        artist.Genre = genre;
        var album = CreateAlbum("al1", "Album", artist);
        album.Genre = genre;
        var song = CreateSong("s1", "Song", album, artist);
        song.Genre = genre;
        CreateSong("s2", "Other", album, artist);
        Library.SaveContext();
        Assert.Single(Library.QueryGenreArtists(genre));
        Assert.Single(Library.QueryGenreAlbums(genre));
        Assert.Equal(["Song"], Library.QueryGenreSongs(genre).Select(s => s.TitleRaw).ToList());
        Assert.Empty(Library.QueryGenreSongs(genre, onlyCached: true));
    }

    [Fact]
    public void DirectoryQueries()
    {
        var folder = Library.CreateMusicFolder(Account);
        folder.Id = "f";
        folder.Name = "Music";
        var root = Library.CreateDirectory(Account);
        root.Id = "d1";
        root.Name = "Root";
        root.MusicFolder = folder;
        var sub = Library.CreateDirectory(Account);
        sub.Id = "d2";
        sub.Name = "Sub";
        sub.Parent = root;
        var song = CreateSong("s1", "Song", null, null);
        song.Directory = root;
        Library.SaveContext();
        Assert.Single(Library.QueryMusicFolders(Account));
        Assert.Equal(["Root"], Library.QueryMusicFolderDirectories(folder).Select(d => d.NameRaw).ToList());
        Assert.Equal(["Sub"], Library.QuerySubdirectories(root).Select(d => d.NameRaw).ToList());
        Assert.Empty(Library.QuerySubdirectories(root, "xyz"));
        Assert.Single(Library.QueryDirectorySongs(root));
        Assert.Empty(Library.QueryDirectorySongs(root, onlyCached: true));
    }

    [Fact]
    public void PodcastEpisodesSortedByPublishDate()
    {
        var podcast = Library.CreatePodcast(Account);
        podcast.Id = "p";
        podcast.Title = "Pod";
        for (var i = 0; i < 3; i++)
        {
            var episode = Library.CreatePodcastEpisode(Account);
            episode.Id = $"e{i}";
            episode.Title = $"Episode {i}";
            episode.Podcast = podcast;
            episode.PodcastStatus = PodcastEpisodeRemoteStatus.Completed;
            episode.PublishDate = new DateTime(2020, 1, 1 + i, 0, 0, 0, DateTimeKind.Utc);
        }
        Library.SaveContext();
        Assert.Equal(["Episode 2", "Episode 1", "Episode 0"], Library.QueryPodcastEpisodes(podcast).Select(e => e.TitleRaw).ToList());
    }

    [Fact]
    public void SortPlaylistsAndRadios()
    {
        var p1 = Library.CreatePlaylist(Account);
        p1.Name = "b list";
        p1.LastPlayedDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var p2 = Library.CreatePlaylist(Account);
        p2.Name = "A list";
        p2.LastPlayedDate = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var radio = Library.CreateRadio(Account);
        radio.Id = "r";
        radio.Title = "Radio";
        Library.SaveContext();
        var q = Library.QueryPlaylists(Account, "", PlaylistSearchCategory.All);
        Assert.Equal(["A list", "b list"], LibraryStorage.SortPlaylists(q, PlaylistSortType.Name).Select(p => p.NameRaw).ToList());
        Assert.Equal(["A list", "b list"], LibraryStorage.SortPlaylists(q, PlaylistSortType.LastPlayed).Select(p => p.NameRaw).ToList());
        Assert.Equal(["A", "B"], LibraryStorage.GetSectionInitials(q));
        Assert.Single(Library.QuerySortedRadios(Account));
    }
}
