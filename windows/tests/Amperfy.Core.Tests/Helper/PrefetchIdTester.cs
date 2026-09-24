namespace Amperfy.Core.Tests.Helper;

/// Port of PrefetchIdTester.swift: checks the ids collected by an IDs parser, the prefetched
/// elements for these ids and the element counts in the library (account 1).
public sealed class PrefetchIdTester
{
    private readonly LibraryStorage _library;
    private readonly PrefetchIdContainer _prefetchIDs;

    public PrefetchIdTester(LibraryStorage library, PrefetchIdContainer prefetchIDs)
    {
        _library = library;
        _prefetchIDs = prefetchIDs;
    }

    public void CheckPrefetchIdCounts(
        int artworkCount = 0,
        int genreIdCount = 0,
        int genreNameCount = 0,
        int artistCount = 0,
        int localArtistCount = 0,
        int albumCount = 0,
        int songCount = 0,
        int podcastEpisodeCount = 0,
        int radioCount = 0,
        int musicFolderCount = 0,
        int directoryCount = 0,
        int podcastCount = 0,
        int? artworkFetchCount = null,
        int? genreFetchCount = null,
        int? artistFetchCount = null,
        int? localArtistFetchCount = null,
        int? albumFetchCount = null,
        int? songFetchCount = null,
        int? podcastEpisodeFetchCount = null,
        int? radioFetchCount = null,
        int? musicFolderFetchCount = null,
        int? directoryFetchCount = null,
        int? podcastFetchCount = null,
        int? artworkLibraryCount = null,
        int? genreLibraryCount = null,
        int? artistLibraryCount = null,
        int? albumLibraryCount = null,
        int? songLibraryCount = null,
        int? podcastEpisodeLibraryCount = null,
        int? radioLibraryCount = null,
        int? musicFolderLibraryCount = null,
        int? directoryLibraryCount = null,
        int? podcastLibraryCount = null)
    {
        // CoreData fetches include pending (unsaved) changes, EF Core queries only see the database.
        _library.SaveContext();

        var summedCount = artworkCount + genreIdCount + genreNameCount + artistCount +
                          localArtistCount + albumCount + songCount + podcastEpisodeCount + radioCount +
                          musicFolderCount + directoryCount + podcastCount;

        Assert.Equal(artworkCount, _prefetchIDs.ArtworkIDs.Count);
        Assert.Equal(genreIdCount, _prefetchIDs.GenreIDs.Count);
        Assert.Equal(genreNameCount, _prefetchIDs.GenreNames.Count);
        Assert.Equal(artistCount, _prefetchIDs.ArtistIDs.Count);
        Assert.Equal(localArtistCount, _prefetchIDs.LocalArtistNames.Count);
        Assert.Equal(albumCount, _prefetchIDs.AlbumIDs.Count);
        Assert.Equal(songCount, _prefetchIDs.SongIDs.Count);
        Assert.Equal(podcastEpisodeCount, _prefetchIDs.PodcastEpisodeIDs.Count);
        Assert.Equal(radioCount, _prefetchIDs.RadioIDs.Count);
        Assert.Equal(musicFolderCount, _prefetchIDs.MusicFolderIDs.Count);
        Assert.Equal(directoryCount, _prefetchIDs.DirectoryIDs.Count);
        Assert.Equal(podcastCount, _prefetchIDs.PodcastIDs.Count);
        // summed
        Assert.Equal(summedCount, _prefetchIDs.Counts);

        var actArtworkFetchCount = artworkFetchCount ?? artworkCount;
        var actGenreFetchCount = genreFetchCount ?? (genreIdCount + genreNameCount);
        var actArtistFetchCount = artistFetchCount ?? artistCount;
        var actLocalArtistFetchCount = localArtistFetchCount ?? localArtistCount;
        var actAlbumFetchCount = albumFetchCount ?? albumCount;
        var actSongFetchCount = songFetchCount ?? songCount;
        var actPodcastEpisodeFetchCount = podcastEpisodeFetchCount ?? podcastEpisodeCount;
        var actRadioFetchCount = radioFetchCount ?? radioCount;
        var actMusicFolderFetchCount = musicFolderFetchCount ?? musicFolderCount;
        var actDirectoryFetchCount = directoryFetchCount ?? directoryCount;
        var actPodcastFetchCount = podcastFetchCount ?? podcastCount;

        var summedFetchCount = actArtworkFetchCount + actGenreFetchCount + actArtistFetchCount +
                               actLocalArtistFetchCount + actAlbumFetchCount + actSongFetchCount +
                               actPodcastEpisodeFetchCount + actRadioFetchCount + actMusicFolderFetchCount +
                               actDirectoryFetchCount + actPodcastFetchCount;

        var account = _library.GetAccount(TestAccountInfo.Create1());
        var prefetch = _library.GetElements(account, _prefetchIDs);
        Assert.Equal(actArtworkFetchCount, prefetch.PrefetchedArtworkDict.Count);
        Assert.Equal(actGenreFetchCount, prefetch.PrefetchedGenreDict.Count);
        Assert.Equal(actArtistFetchCount, prefetch.PrefetchedArtistDict.Count);
        Assert.Equal(actLocalArtistFetchCount, prefetch.PrefetchedLocalArtistDict.Count);
        Assert.Equal(actAlbumFetchCount, prefetch.PrefetchedAlbumDict.Count);
        Assert.Equal(actSongFetchCount, prefetch.PrefetchedSongDict.Count);
        Assert.Equal(actPodcastEpisodeFetchCount, prefetch.PrefetchedPodcastEpisodeDict.Count);
        Assert.Equal(actRadioFetchCount, prefetch.PrefetchedRadioDict.Count);
        Assert.Equal(actMusicFolderFetchCount, prefetch.PrefetchedMusicFolderDict.Count);
        Assert.Equal(actDirectoryFetchCount, prefetch.PrefetchedDirectoryDict.Count);
        Assert.Equal(actPodcastFetchCount, prefetch.PrefetchedPodcastDict.Count);
        // summed
        Assert.Equal(summedFetchCount, prefetch.Counts);

        Assert.Equal(artworkLibraryCount ?? actArtworkFetchCount, _library.GetArtworkCount(account));
        Assert.Equal(genreLibraryCount ?? actGenreFetchCount, _library.GetGenreCount(account));
        Assert.Equal(artistLibraryCount ?? (actArtistFetchCount + actLocalArtistFetchCount), _library.GetArtistCount(account));
        Assert.Equal(albumLibraryCount ?? actAlbumFetchCount, _library.GetAlbumCount(account));
        Assert.Equal(songLibraryCount ?? actSongFetchCount, _library.GetSongCount(account));
        Assert.Equal(podcastEpisodeLibraryCount ?? actPodcastEpisodeFetchCount, _library.GetPodcastEpisodeCount(account));
        Assert.Equal(radioLibraryCount ?? actRadioFetchCount, _library.GetRadioCount(account));
        Assert.Equal(musicFolderLibraryCount ?? actMusicFolderFetchCount, _library.GetMusicFolderCount(account));
        Assert.Equal(directoryLibraryCount ?? actDirectoryFetchCount, _library.GetDirectoryCount(account));
        Assert.Equal(podcastLibraryCount ?? actPodcastFetchCount, _library.GetPodcastCount(account));
    }
}
