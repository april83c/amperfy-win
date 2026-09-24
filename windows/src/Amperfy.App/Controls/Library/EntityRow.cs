using Amperfy.App.Helpers;
using Amperfy.App.Library;
using Amperfy.Core.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Amperfy.App.Controls;

/// List row of a container (album, artist, genre, playlist, podcast, directory, music folder)
/// (port of GenericTableCell, PlaylistTableCell, DirectoryTableCell): artwork, title, subtitle,
/// info, favorite and a chevron. Right click opens the entity context menu.
public sealed partial class EntityRow : UserControl
{
    private readonly ArtworkImage _artwork;
    private readonly FontIcon _folderIcon;
    private readonly Grid _lead;
    private readonly TextBlock _title;
    private readonly TextBlock _subtitle;
    private readonly TextBlock _info;
    private readonly FontIcon _favorite;
    private readonly FontIcon _typeIcon;
    private LibraryItem? _item;
    private bool _isSubscribed;

    public EntityRow()
    {
        var root = new Grid { MinHeight = 56, ColumnSpacing = 12, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _lead = new Grid { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Center };
        _artwork = new ArtworkImage { Width = 44, Height = 44, DecodeSize = 88, CornerRadius = new CornerRadius(4) };
        _folderIcon = Ui.Icon(Icons.Folder, 24);
        _folderIcon.HorizontalAlignment = HorizontalAlignment.Center;
        _lead.Children.Add(_artwork);
        _lead.Children.Add(_folderIcon);
        root.Children.Add(_lead);

        var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _typeIcon = Ui.Icon(Icons.Song, 12, secondary: true);
        _title = Ui.Text("", "BodyTextBlockStyle");
        titleRow.Children.Add(_typeIcon);
        titleRow.Children.Add(_title);
        _subtitle = Ui.Text("", "CaptionTextBlockStyle", secondary: true);
        textPanel.Children.Add(titleRow);
        textPanel.Children.Add(_subtitle);
        Grid.SetColumn(textPanel, 1);
        root.Children.Add(textPanel);

        _info = Ui.Text("", "CaptionTextBlockStyle", secondary: true);
        _info.TextAlignment = TextAlignment.Right;
        _info.MaxWidth = 320;
        Grid.SetColumn(_info, 2);
        root.Children.Add(_info);

        _favorite = Ui.Icon(Icons.HeartFill, 12, Ui.FavoriteBrush);
        ToolTipService.SetToolTip(_favorite, "Favorite");
        Grid.SetColumn(_favorite, 3);
        root.Children.Add(_favorite);

        var chevron = Ui.Icon(LibraryGlyphs.ChevronRight, 12, secondary: true);
        Grid.SetColumn(chevron, 4);
        root.Children.Add(chevron);

        Content = root;
        ContextFlyout = EntityActions.CreateMenuFlyout(() =>
        {
            if (_item?.Container is not { } container) return null;
            var item = _item;
            return (container, new EntityActionOptions
            {
                HostPageType = item.Context.HostPageType,
                Changed = OnChanged,
                ExtraItems = item.Context.ExtraMenuItems is { } extra ? () => extra(item) : null,
            });
        });

        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
    }

    public void Bind(LibraryItem item)
    {
        _item = item;
        Refresh();
        Subscribe();
    }

    private void Subscribe()
    {
        if (_isSubscribed || _item is null) return;
        _isSubscribed = true;
        LibraryEventHub.EnsureInitialized();
        LibraryEventHub.EntityChanged += OnEntityChanged;
    }

    private void Unsubscribe()
    {
        if (!_isSubscribed) return;
        _isSubscribed = false;
        LibraryEventHub.EntityChanged -= OnEntityChanged;
    }

    private void OnEntityChanged(object entity)
    {
        if (_item is not null && ReferenceEquals(LibraryItem.Unwrap(_item.Entity), entity)) Refresh();
    }

    private void OnChanged()
    {
        Refresh();
        _item?.Context.Changed?.Invoke();
    }

    public void Refresh()
    {
        if (_item is null) return;
        var entity = LibraryItem.Unwrap(_item.Entity);
        var isHistory = _item.Entity is SearchHistoryItem;
        _typeIcon.Visibility = isHistory ? Visibility.Visible : Visibility.Collapsed;
        _favorite.Visibility = Visibility.Collapsed;
        _info.Text = "";
        _subtitle.Text = "";

        var showArtwork = _item.Context.ShowArtwork && entity is not (Genre or MusicFolder);
        _lead.Visibility = _item.Context.ShowArtwork ? Visibility.Visible : Visibility.Collapsed;
        _folderIcon.Visibility = Visibility.Collapsed;
        _artwork.Visibility = showArtwork ? Visibility.Visible : Visibility.Collapsed;

        switch (entity)
        {
            case MusicFolder folder:
                _title.Text = folder.Name;
                _folderIcon.Glyph = Icons.Folder;
                _folderIcon.Visibility = Visibility.Visible;
                break;
            case MusicDirectory directory:
                _title.Text = directory.Name;
                var hasImage = directory.Artwork?.ImagePath is not null;
                _artwork.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
                _folderIcon.Glyph = Icons.Folder;
                _folderIcon.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;
                if (hasImage) _artwork.Entity = directory;
                ArtworkLoader.Request(directory);
                break;
            case Genre genre:
                _title.Text = genre.Name;
                _info.Text = LibraryText.Info(genre, DetailType.Short);
                _folderIcon.Glyph = Icons.Genre;
                _folderIcon.Visibility = _item.Context.ShowArtwork ? Visibility.Visible : Visibility.Collapsed;
                break;
            case IPlayableContainable container:
                _title.Text = container.Name;
                _subtitle.Text = container is AbstractPlayable playable ? playable.CreatorName : container.Subtitle ?? "";
                _info.Text = LibraryText.Info(container, DetailType.Short);
                _favorite.Visibility = container.IsFavorite ? Visibility.Visible : Visibility.Collapsed;
                if (showArtwork)
                {
                    _artwork.Entity = container;
                    ArtworkLoader.Request(container);
                }
                if (isHistory) _typeIcon.Glyph = TypeGlyph(container);
                break;
            default:
                _title.Text = _item.ToString();
                break;
        }
        _subtitle.Visibility = string.IsNullOrEmpty(_subtitle.Text) ? Visibility.Collapsed : Visibility.Visible;
        _info.Visibility = string.IsNullOrEmpty(_info.Text) ? Visibility.Collapsed : Visibility.Visible;
        ToolTipService.SetToolTip(_title, _title.Text);
    }

    private static string TypeGlyph(IPlayableContainable container) => container switch
    {
        Song => Icons.Song,
        PodcastEpisode or Podcast => Icons.Podcast,
        Album => Icons.Album,
        Artist => Icons.Artist,
        Playlist => Icons.Playlist,
        Genre => Icons.Genre,
        Radio => Icons.Radio,
        _ => Icons.Folder,
    };
}
