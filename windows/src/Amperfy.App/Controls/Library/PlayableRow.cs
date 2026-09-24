using Amperfy.App.Helpers;
using Amperfy.App.Library;
using Amperfy.App.Services;
using Amperfy.Core.Downloads;
using Amperfy.Core.Model;
using Amperfy.Core.Player;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Amperfy.App.Controls;

/// List row of a song, podcast episode or radio (port of PlayableTableCell): artwork or track
/// number, title, artist/album, rating, favorite, cached indicator, duration, download status and
/// a "more" button. Shows the currently playing item.
public sealed partial class PlayableRow : UserControl
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly ArtworkImage _artwork;
    private readonly Grid _lead;
    private readonly TextBlock _trackNumber;
    private readonly Border _playingOverlay;
    private readonly FontIcon _playingIcon;
    private readonly TextBlock _title;
    private readonly TextBlock _subtitle;
    private readonly TextBlock _rating;
    private readonly FontIcon _favorite;
    private readonly FontIcon _cached;
    private readonly TextBlock _duration;
    private readonly Grid _downloadStatus;
    private readonly FontIcon _downloadIcon;
    private readonly ProgressRing _downloadRing;
    private readonly FontIcon _reorderIcon;
    private readonly Button _moreButton;
    private LibraryItem? _item;
    private bool _isSubscribed;

    public PlayableRow()
    {
        var root = new Grid { MinHeight = 56, ColumnSpacing = 12, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 7; i++) root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _lead = new Grid { Width = 40, Height = 40, VerticalAlignment = VerticalAlignment.Center };
        _artwork = new ArtworkImage { Width = 40, Height = 40, DecodeSize = 80, CornerRadius = new CornerRadius(4) };
        _trackNumber = Ui.Text("", "BodyTextBlockStyle", secondary: true);
        _trackNumber.HorizontalAlignment = HorizontalAlignment.Center;
        _playingIcon = Ui.Icon(Icons.Volume, 16);
        _playingOverlay = new Border
        {
            Child = _playingIcon,
            CornerRadius = new CornerRadius(4),
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(150, 0, 0, 0)),
        };
        _lead.Children.Add(_artwork);
        _lead.Children.Add(_trackNumber);
        _lead.Children.Add(_playingOverlay);
        root.Children.Add(_lead);

        var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        _title = Ui.Text("", "BodyTextBlockStyle");
        _subtitle = Ui.Text("", "CaptionTextBlockStyle", secondary: true);
        textPanel.Children.Add(_title);
        textPanel.Children.Add(_subtitle);
        Grid.SetColumn(textPanel, 1);
        root.Children.Add(textPanel);

        _rating = new TextBlock
        {
            FontFamily = Ui.SymbolFont,
            FontSize = 10,
            Foreground = Ui.RatingBrush,
            VerticalAlignment = VerticalAlignment.Center,
            CharacterSpacing = 60,
        };
        Grid.SetColumn(_rating, 2);
        root.Children.Add(_rating);

        _favorite = Ui.Icon(Icons.HeartFill, 12, Ui.FavoriteBrush);
        ToolTipService.SetToolTip(_favorite, "Favorite");
        Grid.SetColumn(_favorite, 3);
        root.Children.Add(_favorite);

        _cached = Ui.Icon(LibraryGlyphs.Cached, 12, secondary: true);
        ToolTipService.SetToolTip(_cached, "Cached");
        Grid.SetColumn(_cached, 4);
        root.Children.Add(_cached);

        _duration = Ui.Text("", "BodyTextBlockStyle", secondary: true);
        _duration.MinWidth = 44;
        _duration.TextAlignment = TextAlignment.Right;
        Grid.SetColumn(_duration, 5);
        root.Children.Add(_duration);

        _downloadStatus = new Grid { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        _downloadIcon = Ui.Icon(LibraryGlyphs.CheckMark, 14);
        _downloadRing = new ProgressRing { Width = 20, Height = 20, IsActive = false, IsIndeterminate = true };
        _downloadStatus.Children.Add(_downloadIcon);
        _downloadStatus.Children.Add(_downloadRing);
        Grid.SetColumn(_downloadStatus, 6);
        root.Children.Add(_downloadStatus);

        _reorderIcon = Ui.Icon("", 14, secondary: true);
        _reorderIcon.Visibility = Visibility.Collapsed;
        ToolTipService.SetToolTip(_reorderIcon, "Drag to reorder");
        Grid.SetColumn(_reorderIcon, 7);
        root.Children.Add(_reorderIcon);

        _moreButton = Ui.IconButton(Icons.More, "More options");
        _moreButton.Flyout = CreateFlyout();
        Grid.SetColumn(_moreButton, 8);
        root.Children.Add(_moreButton);

        Content = root;
        ContextFlyout = CreateFlyout();

        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
    }

    public AbstractPlayable? Playable { get; private set; }

    public void Bind(LibraryItem item)
    {
        _item = item;
        Playable = item.Playable;
        if (Playable is not null) ArtworkLoader.Request(Playable);
        Refresh();
        Subscribe();
    }

    private void Subscribe()
    {
        if (_isSubscribed || _item is null) return;
        _isSubscribed = true;
        LibraryEventHub.EnsureInitialized();
        LibraryEventHub.PlayerChanged += OnPlayerChanged;
        LibraryEventHub.DownloadFinished += OnDownloadFinished;
        LibraryEventHub.EntityChanged += OnEntityChanged;
    }

    private void Unsubscribe()
    {
        if (!_isSubscribed) return;
        _isSubscribed = false;
        LibraryEventHub.PlayerChanged -= OnPlayerChanged;
        LibraryEventHub.DownloadFinished -= OnDownloadFinished;
        LibraryEventHub.EntityChanged -= OnEntityChanged;
    }

    private void OnPlayerChanged() => RefreshPlayingState();

    private void OnDownloadFinished(string id)
    {
        if (Playable is { } p && p.UniqueId() == id) Refresh();
    }

    private void OnEntityChanged(object entity)
    {
        if (Playable is not null) Refresh();
    }

    private MenuFlyout CreateFlyout() => EntityActions.CreateMenuFlyout(() =>
    {
        if (_item is null || Playable is not { } playable) return null;
        // multi selection in the hosting list -> actions for the selection
        if (Ui.FindAncestor<ListViewBase>(this) is { } list && list.SelectedItems.Count > 1 && list.SelectedItems.Contains(_item))
        {
            var playables = list.SelectedItems.OfType<LibraryItem>().Select(i => i.Playable).OfType<AbstractPlayable>().ToList();
            return (new PlayableSelection(playables), new EntityActionOptions { HostPageType = _item.Context.HostPageType, Changed = OnChanged });
        }
        var item = _item;
        return (playable, new EntityActionOptions
        {
            PlayContext = () => item.Context.PlayContextProvider?.Invoke(item) ?? new PlayContext(playable),
            HostPageType = item.Context.HostPageType,
            Changed = OnChanged,
            ExtraItems = item.Context.ExtraMenuItems is { } extra ? () => extra(item) : null,
        });
    });

    private void OnChanged()
    {
        Refresh();
        _item?.Context.Changed?.Invoke();
    }

    public void Refresh()
    {
        if (_item is null || Playable is not { } playable)
        {
            _title.Text = _item?.Entity is Download d ? d.Title : "";
            _subtitle.Text = "";
            return;
        }
        var context = _item.Context;
        var isEditMode = context.IsEditMode;
        var isTrackStyle = context.IsTrackNumberStyle;

        _title.Text = playable.Title;
        ToolTipService.SetToolTip(_title, playable.Title);
        var subtitle = LibraryText.PlayableSubtitle(playable, context.ShowAlbumInSubtitle);
        _subtitle.Text = subtitle;
        _subtitle.Visibility = string.IsNullOrEmpty(subtitle) ? Visibility.Collapsed : Visibility.Visible;

        _artwork.Visibility = isTrackStyle ? Visibility.Collapsed : Visibility.Visible;
        if (!isTrackStyle) _artwork.Entity = playable;
        _trackNumber.Visibility = isTrackStyle ? Visibility.Visible : Visibility.Collapsed;
        _trackNumber.Text = playable.Track > 0 ? playable.Track.ToString() : "";

        var rating = playable is Song song && _services.Settings.User.IsShowRating ? song.Rating : 0;
        _rating.Text = rating > 0 ? new string(LibraryGlyphs.StarFill[0], rating) : "";
        _rating.Visibility = rating > 0 ? Visibility.Visible : Visibility.Collapsed;

        _favorite.Visibility = playable.IsFavorite ? Visibility.Visible : Visibility.Collapsed;
        _cached.Visibility = playable.IsCached ? Visibility.Visible : Visibility.Collapsed;
        _duration.Text = playable.IsRadio ? "" : LibraryText.Duration(playable.Duration);
        _duration.Visibility = playable.IsRadio ? Visibility.Collapsed : Visibility.Visible;

        RefreshDownloadStatus();
        _reorderIcon.Visibility = isEditMode ? Visibility.Visible : Visibility.Collapsed;
        _moreButton.Visibility = isEditMode ? Visibility.Collapsed : Visibility.Visible;
        Opacity = EntityActions.IsPlayable(playable) || isEditMode ? 1.0 : 0.5;
        RefreshPlayingState();
    }

    private void RefreshDownloadStatus()
    {
        if (_item?.Entity is not Download download)
        {
            _downloadStatus.Visibility = Visibility.Collapsed;
            _downloadRing.IsActive = false;
            return;
        }
        _downloadStatus.Visibility = Visibility.Visible;
        _downloadRing.IsActive = false;
        _downloadRing.Visibility = Visibility.Collapsed;
        _downloadIcon.Visibility = Visibility.Visible;
        if (download.Error is { } error)
        {
            _downloadIcon.Glyph = LibraryGlyphs.Error;
            ToolTipService.SetToolTip(_downloadStatus, error.Description());
        }
        else if (download.IsFinishedSuccessfully)
        {
            _downloadIcon.Glyph = LibraryGlyphs.CheckMark;
            ToolTipService.SetToolTip(_downloadStatus, "Downloaded");
        }
        else if (download.IsDownloading)
        {
            _downloadIcon.Visibility = Visibility.Collapsed;
            _downloadRing.Visibility = Visibility.Visible;
            _downloadRing.IsActive = true;
            ToolTipService.SetToolTip(_downloadStatus, "Downloading");
        }
        else
        {
            _downloadIcon.Glyph = LibraryGlyphs.Waiting;
            ToolTipService.SetToolTip(_downloadStatus, "Waiting");
        }
    }

    private void RefreshPlayingState()
    {
        if (Playable is not { } playable) return;
        AbstractPlayable? current = null;
        var isPlaying = false;
        try
        {
            current = _services.Player.CurrentlyPlaying;
            isPlaying = _services.Player.IsPlaying;
        }
        catch
        {
            // player not ready
        }
        var isCurrent = current is not null && ReferenceEquals(current, playable);
        if (isCurrent) _title.Foreground = Ui.ThemeAccent;
        else _title.ClearValue(TextBlock.ForegroundProperty);
        _playingOverlay.Visibility = isCurrent ? Visibility.Visible : Visibility.Collapsed;
        _playingIcon.Glyph = isPlaying ? Icons.Volume : Icons.Pause;
        if (_item?.Context.IsTrackNumberStyle == true)
        {
            _playingOverlay.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            _playingIcon.Foreground = Ui.ThemeAccent;
            _trackNumber.Visibility = isCurrent ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            _playingOverlay.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(150, 0, 0, 0));
            _playingIcon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
        }
    }
}
