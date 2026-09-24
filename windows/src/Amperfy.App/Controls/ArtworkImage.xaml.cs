using Amperfy.App.Helpers;
using Amperfy.Core.Model;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Amperfy.App.Controls;

/// Shows the artwork of a library entity / playable container (custom image, embedded image or
/// the default placeholder in the account theme color; playlists show up to four images).
public sealed partial class ArtworkImage : UserControl
{
    public static readonly DependencyProperty EntityProperty = DependencyProperty.Register(
        nameof(Entity), typeof(object), typeof(ArtworkImage), new PropertyMetadata(null, (d, _) => ((ArtworkImage)d).Refresh()));

    public static readonly DependencyProperty DecodeSizeProperty = DependencyProperty.Register(
        nameof(DecodeSize), typeof(int), typeof(ArtworkImage), new PropertyMetadata(0, (d, _) => ((ArtworkImage)d).Refresh()));

    /// An AbstractLibraryEntity or IPlayableContainable (e.g. Playlist).
    public object? Entity
    {
        get => GetValue(EntityProperty);
        set => SetValue(EntityProperty, value);
    }

    /// Decode pixel size (0 = automatic from layout size).
    public int DecodeSize
    {
        get => (int)GetValue(DecodeSizeProperty);
        set => SetValue(DecodeSizeProperty, value);
    }

    /// Resolves settings (artwork display preference, theme). Set once by the app.
    public static Func<Account?, (ArtworkDisplayPreference Display, ThemePreference Theme)>? SettingsProvider { get; set; }

    /// Raised globally when an artwork file was downloaded (so visible images can refresh).
    public static event Action? ArtworkChanged;

    public static void NotifyArtworkChanged() => ArtworkChanged?.Invoke();

    public ArtworkImage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ArtworkChanged += OnArtworkChanged;
            ActualThemeChanged += OnThemeChanged;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            ArtworkChanged -= OnArtworkChanged;
            ActualThemeChanged -= OnThemeChanged;
        };
    }

    private void OnArtworkChanged() => DispatcherQueue.TryEnqueue(Refresh);
    private void OnThemeChanged(FrameworkElement sender, object args) => Refresh();

    private int EffectiveDecodeSize
    {
        get
        {
            if (DecodeSize > 0) return DecodeSize;
            var size = Math.Max(Width, Height);
            if (double.IsNaN(size) || size <= 0) size = 200;
            var scale = XamlRoot?.RasterizationScale ?? 1.0;
            return (int)Math.Ceiling(size * scale);
        }
    }

    public void Refresh()
    {
        try
        {
            var entity = Entity;
            Account? account = entity switch
            {
                AbstractLibraryEntity e => e.Account,
                Playlist p => p.Account,
                _ => null,
            };
            var (display, theme) = SettingsProvider?.Invoke(account) ?? (ArtworkDisplayPreference.PreferId3Tag, ThemePreference.Blue);
            var decode = EffectiveDecodeSize;

            ArtworkCollection? collection = entity switch
            {
                IPlayableContainable c => c.GetArtworkCollection(),
                AbstractLibraryEntity e => new ArtworkCollection(e.DefaultArtworkType, e),
                _ => null,
            };
            if (collection is null)
            {
                ShowSingle(ThemeHelper.DefaultArtworkUri(ArtworkType.Song, theme, ActualTheme), decode);
                return;
            }

            if (collection.QuadImageEntity is { Count: >= 4 } quad)
            {
                var uris = quad.Take(4).Select(e => ImageUri(e, display) ?? ThemeHelper.DefaultArtworkUri(e.DefaultArtworkType, theme, ActualTheme)).ToList();
                var key = string.Join("|", uris) + "|" + decode;
                if (key == _shownKey) return;
                _shownKey = key;
                QuadGrid.Visibility = Visibility.Visible;
                SingleImage.Visibility = Visibility.Collapsed;
                Image[] targets = [Quad0, Quad1, Quad2, Quad3];
                for (var i = 0; i < 4; i++) targets[i].Source = CreateBitmap(uris[i], decode / 2);
                return;
            }

            var uri = collection.SingleImageEntity is { } single ? ImageUri(single, display) : null;
            ShowSingle(uri ?? ThemeHelper.DefaultArtworkUri(collection.DefaultArtworkType, theme, ActualTheme), decode);
        }
        catch (Exception ex)
        {
            CrashLog.Write($"ArtworkImage refresh failed: {ex.Message}");
        }
    }

    private static Uri? ImageUri(AbstractLibraryEntity entity, ArtworkDisplayPreference display)
    {
        var path = entity.ImagePath(display);
        return path is not null && File.Exists(path) ? new Uri(path) : null;
    }

    // image(s) currently shown: a refresh with the same source doesn't decode the image again
    private string? _shownKey;

    private void ShowSingle(Uri uri, int decode)
    {
        var key = uri + "|" + decode;
        if (key == _shownKey) return;
        _shownKey = key;
        QuadGrid.Visibility = Visibility.Collapsed;
        SingleImage.Visibility = Visibility.Visible;
        SingleImage.Source = CreateBitmap(uri, decode);
    }

    private static BitmapImage CreateBitmap(Uri uri, int decode)
    {
        var bmp = new BitmapImage { DecodePixelType = DecodePixelType.Physical };
        if (decode > 0) bmp.DecodePixelWidth = decode;
        bmp.UriSource = uri;
        return bmp;
    }
}
