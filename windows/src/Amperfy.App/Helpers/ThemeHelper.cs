using Amperfy.Core.Model;
using Amperfy.Core.Storage;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace Amperfy.App.Helpers;

public static class ThemeHelper
{
    public static Color AccentColor(this ThemePreference theme) => theme switch
    {
        ThemePreference.Blue => Color.FromArgb(255, 0, 122, 255),
        ThemePreference.Green => Color.FromArgb(255, 52, 199, 89),
        ThemePreference.Red => Color.FromArgb(255, 255, 59, 48),
        ThemePreference.Yellow => Color.FromArgb(255, 255, 204, 0),
        ThemePreference.Orange => Color.FromArgb(255, 255, 149, 0),
        ThemePreference.Purple => Color.FromArgb(255, 175, 82, 222),
        _ => Color.FromArgb(255, 0, 122, 255),
    };

    public static Color ContrastColor(this ThemePreference theme) => theme == ThemePreference.Yellow ? Colors.Black : Colors.White;

    private static string ArtworkName(ArtworkType type) => type switch
    {
        ArtworkType.Song => "Song",
        ArtworkType.Album => "Album",
        ArtworkType.Genre => "Genre",
        ArtworkType.Artist => "Artist",
        ArtworkType.Podcast => "Podcast",
        ArtworkType.PodcastEpisode => "PodcastEpisode",
        ArtworkType.Playlist => "Playlist",
        ArtworkType.Folder => "Folder",
        ArtworkType.Radio => "Radio",
        _ => "Song",
    };

    /// Default (placeholder) artwork image for an artwork type in the account theme color.
    public static Uri DefaultArtworkUri(ArtworkType type, ThemePreference theme, ElementTheme elementTheme)
    {
        var isDark = elementTheme == ElementTheme.Dark ||
                     (elementTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
        var file = Path.Combine(AppContext.BaseDirectory, "Assets", "Artwork", $"{theme}{ArtworkName(type)}{(isDark ? "Dark" : "Light")}.png");
        return new Uri(file);
    }
}
