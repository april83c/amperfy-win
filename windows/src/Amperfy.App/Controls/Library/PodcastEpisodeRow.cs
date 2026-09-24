using Amperfy.App.Helpers;
using Amperfy.App.Library;
using Amperfy.App.Services;
using Amperfy.Core.Common;
using Amperfy.Core.Downloads;
using Amperfy.Core.Model;
using Amperfy.Core.Player;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Amperfy.App.Controls;

/// List row of a podcast episode (port of PodcastEpisodeTableCell): artwork, title, publish date,
/// description, play progress, cached indicator and buttons for play, description and more.
public sealed partial class PodcastEpisodeRow : UserControl
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly ArtworkImage _artwork;
    private readonly TextBlock _title;
    private readonly TextBlock _info;
    private readonly TextBlock _description;
    private readonly ProgressBar _progress;
    private readonly TextBlock _progressText;
    private readonly FontIcon _cached;
    private readonly Button _playButton;
    private readonly FontIcon _playIcon;
    private LibraryItem? _item;
    private PodcastEpisode? _episode;
    private bool _isSubscribed;

    public PodcastEpisodeRow()
    {
        var root = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 8, 0, 8), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _artwork = new ArtworkImage { Width = 64, Height = 64, DecodeSize = 128, CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Top };
        root.Children.Add(_artwork);

        var content = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        _info = Ui.Text("", "CaptionTextBlockStyle", secondary: true);
        _title = Ui.Text("", "BodyStrongTextBlockStyle", maxLines: 2);
        _description = Ui.Text("", "CaptionTextBlockStyle", secondary: true, maxLines: 2);
        var progressRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        _progress = new ProgressBar { Width = 80, Minimum = 0, Maximum = 1, VerticalAlignment = VerticalAlignment.Center };
        _progressText = Ui.Text("", "CaptionTextBlockStyle", secondary: true);
        _cached = Ui.Icon(LibraryGlyphs.Cached, 12, secondary: true);
        ToolTipService.SetToolTip(_cached, "Cached");
        progressRow.Children.Add(_progress);
        progressRow.Children.Add(_progressText);
        progressRow.Children.Add(_cached);
        content.Children.Add(_info);
        content.Children.Add(_title);
        content.Children.Add(_description);
        content.Children.Add(progressRow);
        Grid.SetColumn(content, 1);
        root.Children.Add(content);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _playIcon = Ui.Icon(Icons.Play, 16);
        _playButton = new Button
        {
            Content = _playIcon,
            Padding = new Thickness(8),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(_playButton, "Play Episode");
        _playButton.Click += (_, _) => PlayEpisode();
        var descriptionButton = Ui.IconButton(Icons.Info, "Show Episode Description", (_, _) =>
        {
            if (_episode is { } e) _ = EntityActions.ShowDescriptionAsync(e);
        });
        var moreButton = Ui.IconButton(Icons.More, "More options");
        moreButton.Flyout = CreateFlyout();
        buttons.Children.Add(_playButton);
        buttons.Children.Add(descriptionButton);
        buttons.Children.Add(moreButton);
        Grid.SetColumn(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
        ContextFlyout = CreateFlyout();
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
    }

    public void Bind(LibraryItem item)
    {
        _item = item;
        _episode = item.Playable as PodcastEpisode;
        if (_episode is not null) ArtworkLoader.Request(_episode);
        Refresh();
        Subscribe();
    }

    private void Subscribe()
    {
        if (_isSubscribed || _item is null) return;
        _isSubscribed = true;
        LibraryEventHub.EnsureInitialized();
        LibraryEventHub.PlayerChanged += Refresh;
        LibraryEventHub.DownloadFinished += OnDownloadFinished;
        LibraryEventHub.EntityChanged += OnEntityChanged;
    }

    private void Unsubscribe()
    {
        if (!_isSubscribed) return;
        _isSubscribed = false;
        LibraryEventHub.PlayerChanged -= Refresh;
        LibraryEventHub.DownloadFinished -= OnDownloadFinished;
        LibraryEventHub.EntityChanged -= OnEntityChanged;
    }

    private void OnDownloadFinished(string id)
    {
        if (_episode is { } e && e.UniqueId() == id) Refresh();
    }

    private void OnEntityChanged(object entity) => Refresh();

    private MenuFlyout CreateFlyout() => EntityActions.CreateMenuFlyout(() =>
    {
        if (_item is null || _episode is not { } episode) return null;
        var item = _item;
        return (episode, new EntityActionOptions
        {
            PlayContext = () => item.Context.PlayContextProvider?.Invoke(item) ?? new PlayContext(episode),
            HostPageType = item.Context.HostPageType,
            Changed = () =>
            {
                Refresh();
                item.Context.Changed?.Invoke();
            },
        });
    });

    private void PlayEpisode()
    {
        if (_item is null || _episode is not { } episode || !EntityActions.IsPlayable(episode)) return;
        EntityActions.Play(_item.Context.PlayContextProvider?.Invoke(_item) ?? new PlayContext(episode));
    }

    public void Refresh()
    {
        if (_episode is not { } episode) return;
        _artwork.Entity = episode;
        _title.Text = episode.Title;
        ToolTipService.SetToolTip(_title, episode.Title);
        var info = episode.PublishDate.AsShortDayMonthString();
        if (_item?.Context.HostPageType != typeof(Pages.PodcastDetailPage)) info = $"{episode.CreatorName}{LibraryText.Dot}{info}";
        _info.Text = info;
        var description = (episode.Depiction ?? "").Html2String();
        _description.Text = description;
        _description.Visibility = string.IsNullOrEmpty(description) ? Visibility.Collapsed : Visibility.Visible;

        string progressText;
        if (episode.RemainingTimeInSec is { } remaining && episode.PlayProgressPercent is { } percent)
        {
            progressText = $"{remaining.AsDurationString()} left";
            _progress.Visibility = Visibility.Visible;
            _progress.Value = percent;
        }
        else
        {
            progressText = episode.Duration > 0 ? episode.Duration.AsDurationString() : "";
            _progress.Visibility = Visibility.Collapsed;
        }
        if (!episode.IsAvailableToUser())
            progressText += (progressText.Length > 0 ? LibraryText.Dot : "") + episode.UserStatus.Description();
        _progressText.Text = progressText;
        _cached.Visibility = episode.IsCached ? Visibility.Visible : Visibility.Collapsed;

        AbstractPlayable? current = null;
        try { current = _services.Player.CurrentlyPlaying; } catch { /* player not ready */ }
        if (current is not null && ReferenceEquals(current, episode))
        {
            _playIcon.Glyph = Icons.Volume;
            _playIcon.Foreground = Ui.ThemeAccent;
            _playButton.IsEnabled = false;
            _title.Foreground = Ui.ThemeAccent;
        }
        else
        {
            _playIcon.ClearValue(FontIcon.ForegroundProperty);
            _title.ClearValue(TextBlock.ForegroundProperty);
            var playable = EntityActions.IsPlayable(episode);
            _playIcon.Glyph = playable ? Icons.Play : LibraryGlyphs.Ban;
            _playButton.IsEnabled = playable;
        }
    }
}
