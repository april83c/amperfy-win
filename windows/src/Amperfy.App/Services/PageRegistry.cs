using Amperfy.App.Pages;
using Amperfy.Core.Model;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml.Controls;

namespace Amperfy.App.Services;

/// Implemented by pages that show a sidebar library category (to highlight the sidebar entry).
public interface ILibraryCategoryPage
{
    LibraryDisplayType LibraryType { get; }
}

/// Maps library categories and entities to their pages.
public static class PageRegistry
{
    /// Page + navigation parameter of a sidebar library category. The parameter is the
    /// LibraryDisplayType itself; pages covering several categories (e.g. AlbumsPage for all,
    /// favorite, newest and recent albums) switch on it.
    public static (Type Page, object? Parameter) ForLibraryType(LibraryDisplayType type) => type switch
    {
        LibraryDisplayType.Artists or LibraryDisplayType.FavoriteArtists => (typeof(ArtistsPage), type),
        LibraryDisplayType.Albums or LibraryDisplayType.FavoriteAlbums or LibraryDisplayType.NewestAlbums
            or LibraryDisplayType.RecentAlbums => (typeof(AlbumsPage), type),
        LibraryDisplayType.Songs or LibraryDisplayType.FavoriteSongs => (typeof(SongsPage), type),
        LibraryDisplayType.Genres => (typeof(GenresPage), type),
        LibraryDisplayType.Directories => (typeof(MusicFoldersPage), type),
        LibraryDisplayType.Playlists => (typeof(PlaylistsPage), type),
        LibraryDisplayType.Podcasts => (typeof(PodcastsPage), type),
        LibraryDisplayType.Downloads => (typeof(DownloadsPage), type),
        LibraryDisplayType.Radios => (typeof(RadiosPage), type),
        _ => (typeof(HomePage), null),
    };

    /// Detail page of a library entity (null if the entity has no detail page, e.g. a song).
    public static (Type Page, object Parameter)? ForEntity(object entity) => entity switch
    {
        Artist a => (typeof(ArtistDetailPage), a),
        Album a => (typeof(AlbumDetailPage), a),
        Genre g => (typeof(GenreDetailPage), g),
        Playlist p => (typeof(PlaylistDetailPage), p),
        Podcast p => (typeof(PodcastDetailPage), p),
        MusicFolder f => (typeof(IndexesPage), f),
        MusicDirectory d => (typeof(DirectoryPage), d),
        _ => null,
    };

    public static LibraryDisplayType? LibraryTypeOf(Frame frame) => (frame.Content as ILibraryCategoryPage)?.LibraryType;
}
