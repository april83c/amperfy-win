using System.Collections.ObjectModel;
using Amperfy.App.Helpers;
using Amperfy.App.Library;
using Amperfy.App.Services;
using Amperfy.App.Services.Player;
using Amperfy.Core.Common;
using Amperfy.Core.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Amperfy.App.Controls.Player;

public enum QueueRowKind
{
    Header,
    Current,
    Item,
}

/// One row of the queue list: a section header, the currently playing item or a queue item.
public sealed class QueueRow
{
    public QueueRowKind Kind { get; init; }
    public PlayerQueueType Section { get; init; }
    public int Index { get; init; }
    public AbstractPlayable? Playable { get; init; }
    public string Title { get; init; } = "";
    public string? Subtitle { get; init; }

    public PlayerIndex PlayerIndex => new(Section, Index);

    public override string ToString() => Title;
}

/// The player queue (port of QueueVC / the PopupPlayer table): sections "Previous", the currently playing item,
/// "Next in Queue" (user queue) and "Next" (context queue; the podcast queue in podcast mode).
/// Double click / Enter plays an item, Delete removes it, drag &amp; drop reorders (<see cref="IPlayerFacade.MovePlayable"/>),
/// right click opens the shared entity context menu (<see cref="EntityActions"/>) with the queue actions.
public sealed partial class QueueView : UserControl
{
    private const string TemplateXaml =
        "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Grid /></DataTemplate>";

    private static DataTemplate? _itemTemplate;

    private readonly PlayerObserver _observer = new();
    private readonly ListView _list;
    private readonly Grid _titleBar;
    private readonly TextBlock _subtitle;
    private ObservableCollection<QueueRow> _rows = [];
    private QueueRow? _draggedRow;
    private AbstractPlayable? _lastCurrent;
    private bool _isRebuildPending;
    private bool _isDirty = true;
    private bool _isLoaded;

    public QueueView()
    {
        _list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            CanDragItems = true,
            CanReorderItems = true,
            AllowDrop = true,
            IsItemClickEnabled = false,
            ItemTemplate = ItemTemplate,
            Padding = new Thickness(0, 0, 0, 12),
        };
        _list.ContainerContentChanging += List_ContainerContentChanging;
        _list.DragItemsStarting += List_DragItemsStarting;
        _list.DragItemsCompleted += List_DragItemsCompleted;
        _list.DoubleTapped += List_DoubleTapped;
        _list.KeyDown += List_KeyDown;
        _list.ContextRequested += List_ContextRequested;

        var title = new TextBlock { Text = "Queue", VerticalAlignment = VerticalAlignment.Center };
        if (Application.Current.Resources.TryGetValue("SubtitleTextBlockStyle", out var style) && style is Style s) title.Style = s;
        _subtitle = new TextBlock { FontSize = 12, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis };
        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(title);
        titleStack.Children.Add(_subtitle);
        var more = PlayerUi.CreateIconButton(Icons.More, "Queue options", () => { }, 32);
        more.Flyout = PlayerUi.CreatePlayerOptionsFlyout(ScrollToCurrent);
        var close = PlayerUi.CreateIconButton(Icons.Clear, "Close", () => PlayerUi.ShowQueuePane(false), 32);
        _titleBar = new Grid { Padding = new Thickness(16, 12, 8, 8), ColumnSpacing = 4 };
        _titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(more, 1);
        Grid.SetColumn(close, 2);
        _titleBar.Children.Add(titleStack);
        _titleBar.Children.Add(more);
        _titleBar.Children.Add(close);
        _titleBar.Visibility = Visibility.Collapsed;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_list, 1);
        root.Children.Add(_titleBar);
        root.Children.Add(_list);
        Content = root;

        _observer.StartedPlaying += ScheduleRebuild;
        _observer.Stopped += ScheduleRebuild;
        _observer.PlaylistChanged += ScheduleRebuild;
        _observer.ShuffleChanged += ScheduleRebuild;
        _observer.RepeatChanged += ScheduleRebuild;
        _observer.NowPlayingInfoChanged += ScheduleRebuild;
        Loaded += (_, _) =>
        {
            _isLoaded = true;
            _observer.Register().IsActive = true;
            PlayerUi.UiStateChanged += OnUiStateChanged;
            _titleBar.Visibility = ShowsTitle ? Visibility.Visible : Visibility.Collapsed;
            if (_isDirty) Rebuild(scrollToCurrent: true);
        };
        Unloaded += (_, _) =>
        {
            _isLoaded = false;
            _observer.IsActive = false;
            PlayerUi.UiStateChanged -= OnUiStateChanged;
            _isDirty = true;
        };
    }

    /// Shows the "Queue" title bar with options and close button (side pane).
    public bool ShowsTitle { get; set; }

    private static DataTemplate ItemTemplate => _itemTemplate ??= (DataTemplate)XamlReader.Load(TemplateXaml);

    private void OnUiStateChanged() => ScheduleRebuild();

    private void ScheduleRebuild()
    {
        if (!_isLoaded)
        {
            _isDirty = true;
            return;
        }
        if (_isRebuildPending) return;
        _isRebuildPending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _isRebuildPending = false;
            if (_isLoaded) Rebuild(scrollToCurrent: false);
        });
    }

    // --- rows ---------------------------------------------------------------------------------

    private static List<QueueRow> BuildRows()
    {
        var player = PlayerUi.Player;
        var rows = new List<QueueRow>();
        var isMusic = player.PlayerMode == PlayerMode.Music;

        var prev = player.GetAllPrevQueueItems();
        rows.Add(new QueueRow
        {
            Kind = QueueRowKind.Header,
            Section = PlayerQueueType.Prev,
            Title = "Previous",
            Subtitle = prev.Count > 0 ? $"{prev.Count} {(prev.Count == 1 ? "item" : "items")}" : null,
        });
        for (var i = 0; i < prev.Count; i++) rows.Add(ItemRow(PlayerQueueType.Prev, i, prev[i]));

        if (player.CurrentlyPlaying is { } current)
        {
            var info = PlayerUi.CurrentInfo();
            rows.Add(new QueueRow
            {
                Kind = QueueRowKind.Current,
                Section = PlayerQueueType.Next,
                Index = -1,
                Playable = current,
                Title = info.Title,
                Subtitle = info.Artist,
            });
        }

        if (isMusic)
        {
            var user = player.GetAllUserQueueItems();
            if (player.UserQueueCount > 0 && user.Count > 0)
            {
                rows.Add(new QueueRow { Kind = QueueRowKind.Header, Section = PlayerQueueType.User, Title = PlayerQueueType.User.Description() });
                for (var i = 0; i < user.Count; i++) rows.Add(ItemRow(PlayerQueueType.User, i, user[i]));
            }
        }

        var next = player.GetAllNextQueueItems();
        var contextName = isMusic ? player.ContextName : "";
        rows.Add(new QueueRow
        {
            Kind = QueueRowKind.Header,
            Section = PlayerQueueType.Next,
            Title = "Next",
            Subtitle = string.IsNullOrEmpty(contextName) ? null : $"From: {contextName}",
        });
        for (var i = 0; i < next.Count; i++) rows.Add(ItemRow(PlayerQueueType.Next, i, next[i]));
        return rows;
    }

    private static QueueRow ItemRow(PlayerQueueType section, int index, AbstractPlayable playable) => new()
    {
        Kind = QueueRowKind.Item,
        Section = section,
        Index = index,
        Playable = playable,
        Title = playable.Title,
        Subtitle = playable.CreatorName,
    };

    private void Rebuild(bool scrollToCurrent)
    {
        List<QueueRow> rows;
        try
        {
            rows = BuildRows();
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning("QueueView", $"Building the queue failed: {ex.Message}");
            return;
        }
        _isDirty = false;
        var current = PlayerUi.Player.CurrentlyPlaying;
        var currentChanged = !ReferenceEquals(current, _lastCurrent);
        _lastCurrent = current;
        _rows = new ObservableCollection<QueueRow>(rows);
        _list.ItemsSource = _rows;

        var player = PlayerUi.Player;
        var remaining = player.RemainingPlayDuration;
        _subtitle.Text = player.CurrentlyPlaying is null && player.NextQueueCount == 0
            ? "Empty"
            : $"{player.PrevQueueCount + player.UserQueueCount + player.NextQueueCount + (player.CurrentlyPlaying is null ? 0 : 1)} items · {remaining.AsDurationString()} remaining";
        if (scrollToCurrent || currentChanged) ScrollToCurrent();
    }

    /// Scrolls the currently playing row to the top.
    public void ScrollToCurrent()
    {
        var row = _rows.FirstOrDefault(r => r.Kind == QueueRowKind.Current)
                  ?? _rows.FirstOrDefault(r => r.Kind == QueueRowKind.Header && r.Section == PlayerQueueType.Next);
        if (row is null) return;
        // the previous-section header above keeps the context visible
        var index = _rows.IndexOf(row);
        var target = index > 0 ? _rows[index - 1] : row;
        DispatcherQueue.TryEnqueue(() =>
        {
            try { _list.ScrollIntoView(target, ScrollIntoViewAlignment.Leading); }
            catch (Exception) { /* list not ready */ }
        });
    }

    // --- item presentation --------------------------------------------------------------------

    private void List_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) return;
        if (args.ItemContainer.ContentTemplateRoot is not Grid root || args.Item is not QueueRow row) return;
        if (root.Tag is not RowPresenter presenter)
        {
            presenter = new RowPresenter(root, this);
            root.Tag = presenter;
        }
        presenter.Bind(row);
        args.ItemContainer.IsTabStop = row.Kind != QueueRowKind.Header;
        args.Handled = true;
    }

    /// Builds the visuals of a row once per container and updates them on (re)use.
    private sealed class RowPresenter
    {
        private readonly QueueView _owner;
        private readonly Grid _itemPanel;
        private readonly ArtworkImage _artwork;
        private readonly FontIcon _playingIcon;
        private readonly TextBlock _title;
        private readonly TextBlock _subtitle;
        private readonly TextBlock _duration;
        private readonly Grid _headerPanel;
        private readonly TextBlock _headerTitle;
        private readonly TextBlock _headerSubtitle;
        private readonly StackPanel _headerButtons;
        private readonly Button _clear;
        private readonly ToggleButton _shuffle;
        private readonly ToggleButton _repeat;
        private readonly ToggleButton _autoplay;
        private QueueRow? _row;

        public RowPresenter(Grid root, QueueView owner)
        {
            _owner = owner;
            // item layout
            _artwork = new ArtworkImage { Width = 40, Height = 40, DecodeSize = 96, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
            _playingIcon = new FontIcon { Glyph = Icons.Volume, FontSize = 12, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
            _title = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
            _subtitle = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, FontSize = 12, Opacity = 0.7 };
            _duration = new TextBlock { FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            titleRow.Children.Add(_playingIcon);
            titleRow.Children.Add(_title);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
            texts.Children.Add(titleRow);
            texts.Children.Add(_subtitle);
            _itemPanel = new Grid { Padding = new Thickness(0, 4, 0, 4) };
            _itemPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _itemPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _itemPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(texts, 1);
            Grid.SetColumn(_duration, 2);
            _itemPanel.Children.Add(_artwork);
            _itemPanel.Children.Add(texts);
            _itemPanel.Children.Add(_duration);

            // header layout
            _headerTitle = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            _headerSubtitle = new TextBlock { FontSize = 12, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis };
            var headerTexts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            headerTexts.Children.Add(_headerTitle);
            headerTexts.Children.Add(_headerSubtitle);
            _clear = PlayerUi.CreateIconButton(Icons.Delete, "Clear", OnClear, 32, 14);
            _shuffle = PlayerUi.CreateToggleButton(Icons.Shuffle, "Shuffle (Ctrl+H)", () => { PlayerUi.ToggleShuffle(); _owner.ScheduleRebuild(); }, 32, 14);
            _repeat = PlayerUi.CreateToggleButton(Icons.Repeat, "Repeat (Ctrl+T)", () => { PlayerUi.CycleRepeat(); _owner.ScheduleRebuild(); }, 32, 14);
            _autoplay = PlayerUi.CreateToggleButton(PlayerGlyphs.Autoplay, "Autoplay", PlayerUi.ToggleAutoplay, 32, 14);
            _headerButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            _headerButtons.Children.Add(_autoplay);
            _headerButtons.Children.Add(_shuffle);
            _headerButtons.Children.Add(_repeat);
            _headerButtons.Children.Add(_clear);
            _headerPanel = new Grid { Padding = new Thickness(0, 10, 0, 2) };
            _headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_headerButtons, 1);
            _headerPanel.Children.Add(headerTexts);
            _headerPanel.Children.Add(_headerButtons);

            root.Children.Add(_itemPanel);
            root.Children.Add(_headerPanel);
        }

        public void Bind(QueueRow row)
        {
            _row = row;
            var player = PlayerUi.Player;
            if (row.Kind == QueueRowKind.Header)
            {
                _itemPanel.Visibility = Visibility.Collapsed;
                _headerPanel.Visibility = Visibility.Visible;
                _headerTitle.Text = row.Title;
                _headerSubtitle.Text = row.Subtitle ?? "";
                _headerSubtitle.Visibility = string.IsNullOrEmpty(row.Subtitle) ? Visibility.Collapsed : Visibility.Visible;
                var isMusic = player.PlayerMode == PlayerMode.Music;
                var isNext = row.Section == PlayerQueueType.Next;
                _shuffle.Visibility = isNext && isMusic ? Visibility.Visible : Visibility.Collapsed;
                _repeat.Visibility = isNext && isMusic ? Visibility.Visible : Visibility.Collapsed;
                _autoplay.Visibility = isNext && isMusic ? Visibility.Visible : Visibility.Collapsed;
                _shuffle.IsChecked = player.IsShuffle;
                _shuffle.IsEnabled = PlayerUi.IsShuffleEnabled;
                _repeat.IsChecked = player.RepeatMode != RepeatMode.Off;
                PlayerUi.SetGlyph(_repeat, PlayerUi.RepeatGlyph, PlayerUi.RepeatTooltip);
                _autoplay.IsChecked = PlayerUi.IsAutoplayEnabled;
                var clearTip = row.Section switch
                {
                    PlayerQueueType.User => "Clear User Queue",
                    PlayerQueueType.Next => isMusic ? "Clear Context Queue" : "Clear Player",
                    _ => "",
                };
                _clear.Visibility = row.Section == PlayerQueueType.User || (isNext && (player.NextQueueCount > 0 || player.CurrentlyPlaying is not null))
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                PlayerUi.SetGlyph(_clear, Icons.Delete, clearTip);
                return;
            }

            _headerPanel.Visibility = Visibility.Collapsed;
            _itemPanel.Visibility = Visibility.Visible;
            var isCurrent = row.Kind == QueueRowKind.Current;
            _artwork.Width = isCurrent ? 52 : 40;
            _artwork.Height = isCurrent ? 52 : 40;
            _artwork.Entity = row.Playable;
            _title.Text = row.Title;
            _subtitle.Text = row.Subtitle ?? "";
            _subtitle.Visibility = string.IsNullOrEmpty(row.Subtitle) ? Visibility.Collapsed : Visibility.Visible;
            _playingIcon.Visibility = isCurrent ? Visibility.Visible : Visibility.Collapsed;
            _playingIcon.Glyph = player.IsPlaying ? Icons.Volume : Icons.Pause;
            if (isCurrent)
            {
                // account theme color (theme independent)
                var brush = new SolidColorBrush(ThemeHelper.AccentColor(AppServices.Instance.ActiveTheme));
                _title.Foreground = brush;
                _playingIcon.Foreground = brush;
            }
            else
            {
                _title.ClearValue(TextBlock.ForegroundProperty);
            }
            _title.FontWeight = isCurrent ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            var duration = row.Playable?.Duration ?? 0;
            _duration.Text = row.Playable is { IsRadio: true } || duration <= 0 ? "" : duration.AsColonDurationString();
            _itemPanel.Opacity = row.Section == PlayerQueueType.Prev && !isCurrent ? 0.6 : 1.0;
        }

        private void OnClear()
        {
            if (_row is null) return;
            switch (_row.Section)
            {
                case PlayerQueueType.User:
                    PlayerUi.ClearUserQueue();
                    break;
                case PlayerQueueType.Next:
                    if (PlayerUi.Player.PlayerMode == PlayerMode.Music) PlayerUi.ClearContextQueue();
                    else PlayerUi.ClearPlayer();
                    break;
            }
        }
    }

    // --- interaction --------------------------------------------------------------------------

    private static QueueRow? RowOf(object? source) => (source as FrameworkElement)?.DataContext as QueueRow;

    private static void Play(QueueRow row)
    {
        if (row.Kind == QueueRowKind.Item) PlayerUi.Player.Play(row.PlayerIndex);
        else if (row.Kind == QueueRowKind.Current && !PlayerUi.Player.IsPlaying) PlayerUi.Player.Play();
    }

    private static void Remove(QueueRow row)
    {
        if (row.Kind != QueueRowKind.Item) return;
        PlayerUi.Player.RemovePlayable(row.PlayerIndex);
        PlayerUi.NotifyQueueModified();
    }

    private void List_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (RowOf(e.OriginalSource) is { } row)
        {
            Play(row);
            e.Handled = true;
        }
    }

    private void List_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_list.SelectedItem is not QueueRow row) return;
        switch (e.Key)
        {
            case VirtualKey.Enter:
                Play(row);
                e.Handled = true;
                break;
            case VirtualKey.Delete:
                Remove(row);
                e.Handled = true;
                break;
        }
    }

    private void List_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Count != 1 || e.Items[0] is not QueueRow { Kind: QueueRowKind.Item } row)
        {
            e.Cancel = true;
            return;
        }
        _draggedRow = row;
    }

    private void List_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        var row = _draggedRow;
        _draggedRow = null;
        if (row is null || args.DropResult != DataPackageOperation.Move) return;
        var target = ComputeDropTarget(_rows, row);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (target is { } to && to != row.PlayerIndex)
            {
                try
                {
                    PlayerUi.Player.MovePlayable(row.PlayerIndex, to);
                }
                catch (Exception ex)
                {
                    AmperfyLog.Warning("QueueView", $"Moving the item failed: {ex.Message}");
                }
            }
            PlayerUi.NotifyQueueModified();
        });
    }

    /// Target index of a moved row, computed from the reordered rows: the section is given by the nearest header
    /// (or the currently playing row, followed by the next section) above it, the index counts the items between.
    public static PlayerIndex? ComputeDropTarget(IReadOnlyList<QueueRow> rows, QueueRow moved)
    {
        var position = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            if (ReferenceEquals(rows[i], moved))
            {
                position = i;
                break;
            }
        }
        if (position < 0) return null;
        var section = PlayerQueueType.Prev;
        var count = 0;
        for (var i = 0; i < position; i++)
        {
            var row = rows[i];
            switch (row.Kind)
            {
                case QueueRowKind.Header:
                    section = row.Section;
                    count = 0;
                    break;
                case QueueRowKind.Current:
                    // items dropped below the current row belong to the section that follows it
                    section = PlayerQueueType.Next;
                    for (var j = i + 1; j < rows.Count; j++)
                    {
                        if (rows[j].Kind == QueueRowKind.Header)
                        {
                            section = rows[j].Section;
                            break;
                        }
                    }
                    count = 0;
                    break;
                case QueueRowKind.Item:
                    count++;
                    break;
            }
        }
        return new PlayerIndex(section, count);
    }

    private void List_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (RowOf(args.OriginalSource) is not { Kind: not QueueRowKind.Header } row || row.Playable is not { } playable) return;
        var flyout = CreateContextFlyout(row, playable);
        if (args.TryGetPosition(_list, out var point)) flyout.ShowAt(_list, point);
        else if (args.OriginalSource is FrameworkElement element) flyout.ShowAt(element);
        args.Handled = true;
    }

    /// The shared entity context menu (Swift: EntityPreviewActionBuilder with playerIndexCb): "Play" jumps to the
    /// queue entry; queue specific items (play next, add to queue, remove) are appended.
    private MenuFlyout CreateContextFlyout(QueueRow row, AbstractPlayable playable)
    {
        var isItem = row.Kind == QueueRowKind.Item;
        return EntityActions.CreateMenuFlyout(playable, new EntityActionOptions
        {
            PlayerIndex = isItem ? () => row.PlayerIndex : null,
            Changed = ScheduleRebuild,
            ExtraItems = () => QueueMenuItems(row, playable),
        });
    }

    private static IEnumerable<MenuFlyoutItemBase> QueueMenuItems(QueueRow row, AbstractPlayable playable)
    {
        var player = PlayerUi.Player;
        if (player.PlayerMode == PlayerMode.Music && EntityActions.IsPlayable(playable) && (playable.IsSong || playable.IsRadio))
        {
            yield return MenuItem("Play Next", PlayerGlyphs.PlayNext, () =>
            {
                player.InsertUserQueue([playable]);
                PlayerUi.NotifyQueueModified();
            });
            yield return MenuItem("Add to Queue", PlayerGlyphs.AddToQueue, () =>
            {
                player.AppendUserQueue([playable]);
                PlayerUi.NotifyQueueModified();
            });
        }
        if (row.Kind == QueueRowKind.Item)
        {
            var remove = MenuItem("Remove from Queue", Icons.Delete, () => Remove(row));
            remove.KeyboardAcceleratorTextOverride = "Del";
            yield return remove;
        }
    }

    private static MenuFlyoutItem MenuItem(string text, string glyph, Action action)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        item.Click += (_, _) => action();
        return item;
    }
}
