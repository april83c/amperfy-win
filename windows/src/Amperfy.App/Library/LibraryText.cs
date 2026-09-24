using Amperfy.App.Services;
using Amperfy.Core.Common;
using Amperfy.Core.Model;

namespace Amperfy.App.Library;

/// Display texts of library entities.
public static class LibraryText
{
    /// IPlayableContainable.Info(...) ("12 Songs · 45m") of the container's account api.
    public static string Info(IPlayableContainable container, DetailType type) =>
        PlayableContainableExtensions.Info(container, container.Account?.ApiType.AsServerApiType(), AppServices.Instance.DetailInfo(type));

    public static string Dot => $" {PlayableContainableExtensions.OneMiddleDot} ";

    public static string TypeName(object entity) => entity switch
    {
        Song => "Song",
        PodcastEpisode => "Podcast Episode",
        Radio => "Radio",
        Album => "Album",
        Artist => "Artist",
        Genre => "Genre",
        Playlist p => p.IsSmartPlaylist ? "Smart Playlist" : "Playlist",
        Podcast => "Podcast",
        MusicDirectory => "Directory",
        MusicFolder => "Music Folder",
        _ => "",
    };

    public static string Title(object? entity) => entity switch
    {
        AbstractPlayable p => p.Title,
        IPlayableContainable c => c.Name,
        MusicFolder f => f.Name,
        _ => "",
    };

    /// Subtitle of a playable row (Swift: creatorName; optionally with album).
    public static string PlayableSubtitle(AbstractPlayable playable, bool withAlbum) => playable switch
    {
        Song song when withAlbum && song.Album is { } album => $"{song.CreatorName}{Dot}{album.Name}",
        Song song => song.CreatorName,
        PodcastEpisode episode => episode.CreatorName,
        Radio radio => radio.SiteUrl ?? "",
        _ => playable.CreatorName,
    };

    public static string Duration(int seconds) => seconds > 0 ? seconds.AsColonDurationString() : "";
}
