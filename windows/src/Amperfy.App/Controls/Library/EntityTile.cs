using Amperfy.App.Helpers;
using Amperfy.App.Library;
using Amperfy.Core.Model;
using Amperfy.Core.Player;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Amperfy.App.Controls;

/// Grid tile of an album, artist, playlist, podcast, genre, song, episode or radio (port of
/// AlbumCollectionCell): square artwork with a play button on hover, title and subtitle.
/// Used in GridViews (DataContext = LibraryItem) and in the home rows (Bind directly).
public sealed partial class EntityTile : UserControl
{
    private readonly Grid _artworkHost;
    private readonly ArtworkImage _artwork;
    private readonly Button _playButton;
    private readonly TextBlock _title;
    private readonly TextBlock _subtitle;
    private readonly FontIcon _favorite;
    private readonly StackPanel _root;
    private LibraryItem? _item;
    private LibraryListContext? _subscribedContext;
    private bool _isSubscribed;

    public EntityTile()
    {
        _root = new StackPanel { Spacing = 4, Padding = new Thickness(4), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        _artworkHost = new Grid();
        _artwork = new ArtworkImage { CornerRadius = new CornerRadius(8) };
        _artworkHost.Children.Add(_artwork);

        var playIcon = Ui.Icon(Icons.Play, 16, new SolidColorBrush(Microsoft.UI.Colors.White));
        _playButton = new Button
        {
            Content = playIcon,
            Width = 40,
            Height = 40,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(20),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(8),
            Background = Ui.ThemeAccent,
            BorderThickness = new Thickness(0),
            Visibility = Visibility.Collapsed,
        };
        ToolTipService.SetToolTip(_playButton, "Play");
        _playButton.Click += (_, _) => PlayItem();
        _artworkHost.Children.Add(_playButton);

        _favorite = Ui.Icon(Icons.HeartFill, 14, Ui.FavoriteBrush);
        _favorite.HorizontalAlignment = HorizontalAlignment.Right;
        _favorite.VerticalAlignment = VerticalAlignment.Top;
        _favorite.Margin = new Thickness(8);
        _artworkHost.Children.Add(_favorite);
        _root.Children.Add(_artworkHost);

        _title = Ui.Text("", "BodyStrongTextBlockStyle");
        _subtitle = Ui.Text("", "CaptionTextBlockStyle", secondary: true);
        _root.Children.Add(_title);
        _root.Children.Add(_subtitle);
        Content = _root;

        Tapped += (_, e) =>
        {
            if (!IsStandalone || _item is null) return;
            if (e.OriginalSource is DependencyObject source && Ui.FindAncestor<Microsoft.UI.Xaml.Controls.Primitives.ButtonBase>(source) is not null) return;
            e.Handled = true;
            (Activated ?? LibraryListController.ActivateDefault)(_item);
        };
        KeyDown += (_, e) =>
        {
            if (!IsStandalone || _item is null || e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true;
            (Activated ?? LibraryListController.ActivateDefault)(_item);
        };
        PointerEntered += (_, _) => _playButton.Visibility = CanPlay ? Visibility.Visible : Visibility.Collapsed;
        PointerExited += (_, _) => _playButton.Visibility = Visibility.Collapsed;
        DataContextChanged += (_, args) =>
        {
            if (args.NewValue is LibraryItem item) Bind(item);
        };
        ContextFlyout = EntityActions.CreateMenuFlyout(() =>
        {
            if (_item?.Container is not { } container) return null;
            var item = _item;
            return (container, new EntityActionOptions
            {
                PlayContext = container is AbstractPlayable playable
                    ? () => item.Context.PlayContextProvider?.Invoke(item) ?? new PlayContext(playable)
                    : null,
                HostPageType = item.Context.HostPageType,
                Changed = Refresh,
            });
        });
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
    }

    /// Fixed tile width (home rows); in grids the width comes from the list context.
    public double TileWidth { get; set; } = double.NaN;

    /// Tile outside of a ListView/GridView (home rows, artist albums): click / Enter activates it.
    public bool IsStandalone
    {
        get => _isStandalone;
        set
        {
            _isStandalone = value;
            IsTabStop = value;
            UseSystemFocusVisuals = value;
        }
    }

    private bool _isStandalone;

    /// Activation of a standalone tile (default: open container / play playable).
    public Action<LibraryItem>? Activated { get; set; }

    public LibraryItem? Item => _item;

    private bool CanPlay => _item?.Container is { } c && (c is not AbstractPlayable p || EntityActions.IsPlayable(p));

    public void Bind(LibraryItem item)
    {
        if (_isSubscribed && _subscribedContext != item.Context) Unsubscribe();
        _item = item;
        if (item.Container is { } container) ArtworkLoader.Request(container);
        Refresh();
        Subscribe();
    }

    private void Subscribe()
    {
        if (_isSubscribed || _item is null) return;
        _isSubscribed = true;
        LibraryEventHub.EnsureInitialized();
        LibraryEventHub.EntityChanged += OnEntityChanged;
        _subscribedContext = _item.Context;
        _subscribedContext.LayoutChanged += Refresh;
    }

    private void Unsubscribe()
    {
        if (!_isSubscribed) return;
        _isSubscribed = false;
        LibraryEventHub.EntityChanged -= OnEntityChanged;
        if (_subscribedContext is not null) _subscribedContext.LayoutChanged -= Refresh;
        _subscribedContext = null;
    }

    private void OnEntityChanged(object entity)
    {
        if (_item is not null && ReferenceEquals(LibraryItem.Unwrap(_item.Entity), entity)) Refresh();
    }

    public void Refresh()
    {
        if (_item is null) return;
        var width = double.IsNaN(TileWidth) ? _item.Context.TileWidth : TileWidth;
        var artworkSize = Math.Max(60, width - 8);
        _root.Width = width;
        _artworkHost.Width = artworkSize;
        _artworkHost.Height = artworkSize;
        _artwork.Width = artworkSize;
        _artwork.Height = artworkSize;
        var entity = LibraryItem.Unwrap(_item.Entity);
        _artwork.Entity = entity;
        _title.Text = LibraryText.Title(entity);
        ToolTipService.SetToolTip(_title, _title.Text);
        _subtitle.Text = entity switch
        {
            AbstractPlayable playable => LibraryText.PlayableSubtitle(playable, withAlbum: false),
            Album album => album.Subtitle ?? "",
            IPlayableContainable container => LibraryText.Info(container, DetailType.Short),
            _ => "",
        };
        _subtitle.Visibility = string.IsNullOrEmpty(_subtitle.Text) ? Visibility.Collapsed : Visibility.Visible;
        _favorite.Visibility = entity is IPlayableContainable { IsFavorite: true } ? Visibility.Visible : Visibility.Collapsed;
        _playButton.Background = Ui.ThemeAccent;
    }

    private void PlayItem()
    {
        if (_item?.Container is not { } container) return;
        if (container is AbstractPlayable playable)
        {
            if (EntityActions.IsPlayable(playable))
                EntityActions.Play(_item.Context.PlayContextProvider?.Invoke(_item) ?? new PlayContext(playable));
            return;
        }
        _ = EntityActions.PlayContainerAsync(container);
    }
}
