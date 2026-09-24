using Microsoft.EntityFrameworkCore;

namespace Amperfy.Core.Storage;

/// Batch loading of the data lists display (album, artist, artworks, ...). Without it every row
/// lazy loads its navigations with one query each, which blocks the UI thread for seconds with
/// big lists (e.g. a playlist with thousands of songs).
public sealed partial class LibraryStorage
{
    /// Loads the items of a playlist with their playables and display data in a few queries.
    public void LoadPlaylistItems(Playlist playlist)
    {
        var items = Context.Entry(playlist).Collection(p => p.ItemsRaw);
        if (!items.IsLoaded)
        {
            items.Query().Include(i => i.Playable).Load();
            items.IsLoaded = true;
            playlist.InvalidateItemCache();
        }
        LoadDisplayData(playlist.ItemsRaw.Select(i => i.Playable));
    }

    /// Loads the navigations used to display the given entities (playables, playlist items,
    /// downloads, albums, artists, genres, podcasts, playlists) for all of them at once.
    public void LoadDisplayData(IEnumerable<object?> entities)
    {
        var songs = new HashSet<int>();
        var episodes = new HashSet<int>();
        var radios = new HashSet<int>();
        var albums = new HashSet<int>();
        var others = new HashSet<int>();
        var playlists = new HashSet<int>();
        foreach (var raw in entities)
        {
            var entity = raw switch
            {
                PlaylistItem i => IsLoaded(i, nameof(PlaylistItem.Playable)) ? i.Playable : null,
                Model.Download d => IsLoaded(d, nameof(Model.Download.Playable)) ? d.Playable : null,
                _ => raw,
            };
            switch (entity)
            {
                case Song s when !IsLoaded(s, nameof(Song.Album), nameof(Song.Artist), nameof(Song.Artwork), nameof(Song.EmbeddedArtwork)):
                    songs.Add(s.Pk);
                    break;
                case PodcastEpisode e when !IsLoaded(e, nameof(PodcastEpisode.Podcast), nameof(PodcastEpisode.Artwork), nameof(PodcastEpisode.EmbeddedArtwork)):
                    episodes.Add(e.Pk);
                    break;
                case Radio r when !IsLoaded(r, nameof(Radio.Artwork)):
                    radios.Add(r.Pk);
                    break;
                case Album a when !IsLoaded(a, nameof(Album.Artist), nameof(Album.Artwork)):
                    albums.Add(a.Pk);
                    break;
                case Playlist p when !Context.Entry(p).Collection(x => x.ArtworkItemsRaw).IsLoaded:
                    playlists.Add(p.Pk);
                    break;
                case AbstractLibraryEntity other when other is not AbstractPlayable && !IsLoaded(other, nameof(AbstractLibraryEntity.Artwork)):
                    others.Add(other.Pk);
                    break;
            }
        }
        if (songs.Count > 0)
            Context.Songs.Where(s => songs.Contains(s.Pk))
                .Include(s => s.Album).Include(s => s.Artist).Include(s => s.Artwork).Include(s => s.EmbeddedArtwork)
                .AsSplitQuery().Load();
        if (episodes.Count > 0)
            Context.PodcastEpisodes.Where(e => episodes.Contains(e.Pk))
                .Include(e => e.Podcast).ThenInclude(p => p!.Artwork).Include(e => e.Artwork).Include(e => e.EmbeddedArtwork)
                .AsSplitQuery().Load();
        if (radios.Count > 0)
            Context.Radios.Where(r => radios.Contains(r.Pk)).Include(r => r.Artwork).Load();
        if (albums.Count > 0)
            Context.Albums.Where(a => albums.Contains(a.Pk)).Include(a => a.Artist).Include(a => a.Artwork).AsSplitQuery().Load();
        if (others.Count > 0)
            Context.LibraryEntities.Where(e => others.Contains(e.Pk)).Include(e => e.Artwork).Load();
        if (playlists.Count > 0)
        {
            Context.Playlists.Where(p => playlists.Contains(p.Pk))
                .Include(p => p.ArtworkItemsRaw).ThenInclude(i => i.Playable).ThenInclude(p => p!.Artwork)
                .Include(p => p.ArtworkItemsRaw).ThenInclude(i => i.Playable).ThenInclude(p => p!.EmbeddedArtwork)
                .AsSplitQuery().Load();
        }
    }

    private bool IsLoaded(object entity, params string[] navigations)
    {
        var entry = Context.Entry(entity);
        foreach (var navigation in navigations)
        {
            if (!entry.Navigation(navigation).IsLoaded) return false;
        }
        return true;
    }
}
