using Amperfy.App.Helpers;
using Amperfy.App.Library;
using Amperfy.App.Services;
using Amperfy.Core.Model;
using Amperfy.Core.Player;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Amperfy.App.Controls;

/// Options of a detail page header.
public sealed class DetailHeaderOptions
{
    /// Play context of the "Play" button (null: all playables of the container).
    public Func<Task<PlayContext?>>? PlayContext { get; init; }

    /// Play context of the "Shuffle" button (null: same as Play).
    public Func<Task<PlayContext?>>? ShuffleContext { get; init; }

    public string PlayText { get; init; } = "Play";
    public bool IsShuffleHidden { get; init; }

    /// Additional text below the info line (podcast description).
    public string? Description { get; init; }

    /// Page showing the header (for the context menu).
    public Type? HostPageType { get; init; }

    /// Called after favorite / rating / cache changes.
    public Action? Changed { get; init; }
}

/// Header of the detail pages (port of GenericDetailTableHeader + LibraryElementDetailTableHeaderView):
/// artwork, type, title (editable for playlists), subtitle (album: link to the artist), info line,
/// description, Play / Shuffle buttons, favorite, rating and the entity menu.
public sealed partial class DetailHeader : UserControl
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly ArtworkImage _artwork;
    private readonly TextBlock _type;
    private readonly TextBlock _title;
    private readonly TextBox _nameBox;
    private readonly HyperlinkButton _subtitleLink;
    private readonly TextBlock _subtitleText;
    private readonly TextBlock _info;
    private readonly TextBlock _description;
    private readonly HyperlinkButton _descriptionMore;
    private readonly Button _playButton;
    private readonly TextBlock _playText;
    private readonly Button _shuffleButton;
    private readonly Button _favoriteButton;
    private readonly FontIcon _favoriteIcon;
    private readonly RatingControl _rating;
    private readonly Button _moreButton;
    private readonly ProgressRing _busy;
    private IPlayableContainable? _container;
    private DetailHeaderOptions _options = new();
    private bool _isUpdatingRating;

    public DetailHeader()
    {
        var root = new Grid { ColumnSpacing = 24, Padding = new Thickness(24, 16, 24, 16) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _artwork = new ArtworkImage { Width = 200, Height = 200, DecodeSize = 400, CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Top };
        root.Children.Add(_artwork);

        var right = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumn(right, 1);
        root.Children.Add(right);

        _type = Ui.Text("", "CaptionTextBlockStyle", secondary: true);
        _title = Ui.Text("", "TitleTextBlockStyle", maxLines: 2);
        _title.IsTextSelectionEnabled = true;
        _nameBox = new TextBox { PlaceholderText = "Playlist name", Visibility = Visibility.Collapsed, MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 280 };
        _subtitleLink = new HyperlinkButton { Padding = new Thickness(0), Visibility = Visibility.Collapsed };
        _subtitleLink.Click += (_, _) =>
        {
            if (_container is Album { Artist: { } artist } album) EntityActions.Open(artist, album);
        };
        ToolTipService.SetToolTip(_subtitleLink, "Show Artist");
        _subtitleText = Ui.Text("", "BodyTextBlockStyle");
        _info = Ui.Text("", "CaptionTextBlockStyle", secondary: true, maxLines: 2);
        _info.IsTextSelectionEnabled = true;
        _description = Ui.Text("", "BodyTextBlockStyle", secondary: true, maxLines: 3);
        _description.MaxWidth = 900;
        _description.HorizontalAlignment = HorizontalAlignment.Left;
        _descriptionMore = new HyperlinkButton { Content = "More", Padding = new Thickness(0), Visibility = Visibility.Collapsed };
        _descriptionMore.Click += (_, _) =>
        {
            if (_container is Podcast podcast) _ = EntityActions.ShowDescriptionAsync(podcast);
            else if (_options.Description is { } d) _ = Library.Dialogs.DialogHelper.ShowTextAsync("Description", d, _container?.Name);
        };

        right.Children.Add(_type);
        right.Children.Add(_title);
        right.Children.Add(_nameBox);
        right.Children.Add(_subtitleLink);
        right.Children.Add(_subtitleText);
        right.Children.Add(_info);
        right.Children.Add(_description);
        right.Children.Add(_descriptionMore);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        _playText = new TextBlock { Text = "Play", VerticalAlignment = VerticalAlignment.Center };
        var playContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        playContent.Children.Add(Ui.Icon(Icons.Play, 14));
        playContent.Children.Add(_playText);
        _playButton = new Button { Content = playContent, MinWidth = 110 };
        if (Ui.Style("AccentButtonStyle") is { } accent) _playButton.Style = accent;
        _playButton.Click += (_, _) => _ = PlayAsync(shuffle: false);
        ToolTipService.SetToolTip(_playButton, "Play");
        _shuffleButton = Ui.TextButton("Shuffle", Icons.Shuffle, (_, _) => _ = PlayAsync(shuffle: true), tooltip: "Shuffle");
        _shuffleButton.MinWidth = 110;
        _favoriteIcon = Ui.Icon(Icons.Heart, 16);
        _favoriteButton = new Button { Content = _favoriteIcon, Padding = new Thickness(10, 8, 10, 8) };
        _favoriteButton.Click += (_, _) => _ = ToggleFavoriteAsync();
        _rating = new RatingControl { IsClearEnabled = true, VerticalAlignment = VerticalAlignment.Center, Caption = "" };
        _rating.ValueChanged += (_, _) => OnRatingChanged();
        ToolTipService.SetToolTip(_rating, "Rating");
        _moreButton = Ui.IconButton(Icons.More, "More options");
        _busy = new ProgressRing { Width = 20, Height = 20, IsActive = false, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(_playButton);
        buttons.Children.Add(_shuffleButton);
        buttons.Children.Add(_favoriteButton);
        buttons.Children.Add(_rating);
        buttons.Children.Add(_moreButton);
        ExtraButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(ExtraButtons);
        buttons.Children.Add(_busy);
        right.Children.Add(buttons);

        Content = root;
        SizeChanged += (_, e) =>
        {
            var size = e.NewSize.Width < 640 ? 120 : 200;
            _artwork.Width = size;
            _artwork.Height = size;
        };
    }

    /// Container for page specific buttons (e.g. playlist "Edit").
    public StackPanel ExtraButtons { get; }

    public IPlayableContainable? Container => _container;

    /// Playlist rename mode: the title becomes a text box.
    public bool IsEditingName
    {
        get => _nameBox.Visibility == Visibility.Visible;
        set
        {
            _nameBox.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            _title.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            if (value)
            {
                _nameBox.Text = _container?.Name ?? "";
                _nameBox.Focus(FocusState.Programmatic);
            }
        }
    }

    public string EditedName => _nameBox.Text ?? "";

    public bool IsBusy
    {
        set
        {
            _busy.IsActive = value;
            _busy.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public void Configure(IPlayableContainable container, DetailHeaderOptions options)
    {
        _container = container;
        _options = options;
        _moreButton.Flyout = EntityActions.CreateMenuFlyout(container, new EntityActionOptions
        {
            HostPageType = options.HostPageType,
            Changed = () =>
            {
                Refresh();
                _options.Changed?.Invoke();
            },
        });
        ArtworkLoader.Request(container);
        Refresh();
    }

    public void Refresh()
    {
        if (_container is not { } container) return;
        var online = _services.Settings.User.IsOnlineMode;
        _artwork.Entity = container;
        _type.Text = LibraryText.TypeName(container).ToUpperInvariant();
        _title.Text = container.Name;
        ToolTipService.SetToolTip(_title, container.Name);

        if (container is Album { Artist: { } artist })
        {
            _subtitleLink.Content = artist.Name;
            _subtitleLink.Visibility = Visibility.Visible;
            _subtitleText.Visibility = Visibility.Collapsed;
        }
        else
        {
            _subtitleLink.Visibility = Visibility.Collapsed;
            _subtitleText.Text = container.Subtitle ?? "";
            _subtitleText.Visibility = string.IsNullOrEmpty(container.Subtitle) ? Visibility.Collapsed : Visibility.Visible;
        }

        var info = LibraryText.Info(container, DetailType.Long);
        _info.Text = info;
        _info.Visibility = string.IsNullOrEmpty(info) ? Visibility.Collapsed : Visibility.Visible;

        var description = _options.Description ?? "";
        _description.Text = description;
        _description.Visibility = string.IsNullOrEmpty(description) ? Visibility.Collapsed : Visibility.Visible;
        _descriptionMore.Visibility = description.Length > 200 ? Visibility.Visible : Visibility.Collapsed;

        _playText.Text = _options.PlayText;
        ToolTipService.SetToolTip(_playButton, _options.PlayText);
        var isPlayPossible = online || container.Playables.HasCachedItems();
        _playButton.IsEnabled = isPlayPossible;
        _shuffleButton.IsEnabled = isPlayPossible;
        _shuffleButton.Visibility = _options.IsShuffleHidden || !_services.Settings.User.IsPlayerShuffleButtonEnabled
            ? Visibility.Collapsed
            : Visibility.Visible;

        _favoriteButton.Visibility = container.IsFavoritable && online ? Visibility.Visible : Visibility.Collapsed;
        _favoriteIcon.Glyph = container.IsFavorite ? Icons.HeartFill : Icons.Heart;
        if (container.IsFavorite) _favoriteIcon.Foreground = Ui.FavoriteBrush;
        else _favoriteIcon.ClearValue(FontIcon.ForegroundProperty);
        ToolTipService.SetToolTip(_favoriteButton, container.IsFavorite ? "Unmark favorite" : "Favorite");

        if (container.IsRateable && online && container is AbstractLibraryEntity entity)
        {
            _rating.Visibility = Visibility.Visible;
            _isUpdatingRating = true;
            _rating.Value = entity.Rating > 0 ? entity.Rating : -1;
            _isUpdatingRating = false;
        }
        else
        {
            _rating.Visibility = Visibility.Collapsed;
        }
    }

    private async Task PlayAsync(bool shuffle)
    {
        if (_container is not { } container) return;
        try
        {
            var provider = shuffle ? _options.ShuffleContext ?? _options.PlayContext : _options.PlayContext;
            if (provider is null)
            {
                await EntityActions.PlayContainerAsync(container, shuffle);
                return;
            }
            var context = await provider();
            if (context is null || context.Playables.Count == 0) return;
            if (shuffle) _services.Player.PlayShuffled(context);
            else _services.Player.Play(context);
        }
        catch (Exception ex)
        {
            _services.EventLogger.Report("Play", ex);
        }
    }

    private async Task ToggleFavoriteAsync()
    {
        if (_container is not { } container) return;
        await EntityActions.ToggleFavoriteAsync(container, () =>
        {
            Refresh();
            _options.Changed?.Invoke();
        });
    }

    private void OnRatingChanged()
    {
        if (_isUpdatingRating || _container is not AbstractLibraryEntity entity) return;
        var value = (int)Math.Round(_rating.Value);
        if (value < 0) value = 0;
        if (value == entity.Rating) return;
        _ = EntityActions.SetRatingAsync(entity, value, () => _options.Changed?.Invoke());
    }
}
