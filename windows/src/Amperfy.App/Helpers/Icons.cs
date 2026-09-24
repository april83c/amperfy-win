using Amperfy.Core.Model;
using Amperfy.Core.Storage;

namespace Amperfy.App.Helpers;

/// Segoe Fluent Icons glyphs used across the app.
public static class Icons
{
    public const string Home = "";
    public const string Search = "";
    public const string Artist = "";
    public const string Album = "";
    public const string Song = "";
    public const string Genre = "";
    public const string Folder = "";
    public const string Playlist = "";
    public const string Podcast = "";
    public const string Download = "";
    public const string HeartFill = "";
    public const string Heart = "";
    public const string Newest = "";
    public const string Recent = "";
    public const string Radio = "";
    public const string Play = "";
    public const string Pause = "";
    public const string Stop = "";
    public const string Next = "";
    public const string Previous = "";
    public const string Shuffle = "";
    public const string Repeat = "";
    public const string RepeatOne = "";
    public const string Queue = "";
    public const string Lyrics = "";
    public const string Volume = "";
    public const string Mute = "";
    public const string Delete = "";
    public const string Add = "";
    public const string Refresh = "";
    public const string Settings = "";
    public const string More = "";
    public const string Info = "";
    public const string Share = "";
    public const string Link = "";
    public const string MiniPlayer = "";
    public const string Edit = "";
    public const string Clear = "";
    public const string Account = "";
    public const string Cloud = "";
    public const string Offline = "";
    public const string Timer = "";
    public const string SkipForward = "";
    public const string SkipBack = "";
    public const string Equalizer = "";

    public static string For(LibraryDisplayType t) => t switch
    {
        LibraryDisplayType.Artists => Artist,
        LibraryDisplayType.Albums => Album,
        LibraryDisplayType.Songs => Song,
        LibraryDisplayType.Genres => Genre,
        LibraryDisplayType.Directories => Folder,
        LibraryDisplayType.Playlists => Playlist,
        LibraryDisplayType.Podcasts => Podcast,
        LibraryDisplayType.Downloads => Download,
        LibraryDisplayType.FavoriteSongs or LibraryDisplayType.FavoriteAlbums or LibraryDisplayType.FavoriteArtists => HeartFill,
        LibraryDisplayType.NewestAlbums => Newest,
        LibraryDisplayType.RecentAlbums => Recent,
        LibraryDisplayType.Radios => Radio,
        _ => Song,
    };

    public static string For(PlayableContainerBaseType t) => t switch
    {
        PlayableContainerBaseType.Song => Song,
        PlayableContainerBaseType.PodcastEpisode => Podcast,
        PlayableContainerBaseType.Album => Album,
        PlayableContainerBaseType.Artist => Artist,
        PlayableContainerBaseType.Genre => Genre,
        PlayableContainerBaseType.Playlist => Playlist,
        PlayableContainerBaseType.Podcast => Podcast,
        PlayableContainerBaseType.Directory => Folder,
        PlayableContainerBaseType.Radio => Radio,
        _ => Song,
    };
}
