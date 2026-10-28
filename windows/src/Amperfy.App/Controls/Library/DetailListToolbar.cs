using Amperfy.App.Helpers;
using Amperfy.App.Library;
using Amperfy.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using VirtualKey = Windows.System.VirtualKey;
using VirtualKeyModifiers = Windows.System.VirtualKeyModifiers;

namespace Amperfy.App.Controls;

/// Toolbar below a detail header: "Jump to" search (finds and scrolls to items without filtering), filter, "Cached" filter (forced in offline mode),
/// refresh and page specific buttons (port of the detail pages' search controller + refresh control).
public sealed partial class DetailListToolbar : UserControl
{
    private readonly TextBox _filterBox;
    private readonly TextBox _findBox;
    private readonly TextBlock _findCount;
    private readonly Button _findPrevious;
    private readonly Button _findNext;
    private readonly StackPanel _findPanel;
    private readonly DispatcherQueueTimer _findTimer;
    private ListFinder? _finder;
    private readonly ToggleButton _cachedToggle;
    private readonly Button _refreshButton;
    private readonly DispatcherQueueTimer _timer;
    private string _lastFilter = "";

    public DetailListToolbar()
    {
        var root = new Grid { ColumnSpacing = 8, Padding = new Thickness(24, 0, 24, 8) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        root.Children.Add(left);

        _findBox = new TextBox { PlaceholderText = "Jump to…", Width = 220, VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(_findBox, "Jump to a song in the list without filtering it (Ctrl+F). Enter / F3: next match, Shift+Enter: previous, Esc: go to the match");
        _findCount = Ui.Text("", "CaptionTextBlockStyle", secondary: true);
        _findCount.VerticalAlignment = VerticalAlignment.Center;
        _findCount.MinWidth = 44;
        _findPrevious = Ui.IconButton(LibraryGlyphs.ChevronUp, "Previous match (Shift+F3)", (_, _) => _finder?.Previous());
        _findNext = Ui.IconButton(LibraryGlyphs.ChevronDown, "Next match (F3)", (_, _) => _finder?.Next());
        _findPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Visibility = Visibility.Collapsed };
        _findPanel.Children.Add(_findBox);
        _findPanel.Children.Add(new Border { Width = 6 });
        _findPanel.Children.Add(_findCount);
        _findPanel.Children.Add(_findPrevious);
        _findPanel.Children.Add(_findNext);

        _filterBox = new TextBox { PlaceholderText = "Filter", Width = 220, VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(_filterBox, "Show only matching items (Ctrl+Shift+F)");
        left.Children.Add(_filterBox);
        left.Children.Add(_findPanel);

        var cachedContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        cachedContent.Children.Add(Ui.Icon(LibraryGlyphs.Cached, 14));
        cachedContent.Children.Add(new TextBlock { Text = "Cached", VerticalAlignment = VerticalAlignment.Center });
        _cachedToggle = new ToggleButton { Content = cachedContent, VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(_cachedToggle, "Show only cached items");
        _cachedToggle.Click += (_, _) => FilterChanged?.Invoke();
        left.Children.Add(_cachedToggle);

        _refreshButton = Ui.IconButton(Icons.Refresh, "Refresh (F5)", (_, _) => RefreshRequested?.Invoke());
        left.Children.Add(_refreshButton);

        Buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(Buttons, 1);
        root.Children.Add(Buttons);
        Content = root;

        _findTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _findTimer.Interval = TimeSpan.FromMilliseconds(150);
        _findTimer.IsRepeating = false;
        _findTimer.Tick += (_, _) => _finder?.Search(_findBox.Text ?? "");
        _findBox.TextChanged += (_, _) =>
        {
            _findTimer.Stop();
            _findTimer.Start();
        };
        _findBox.KeyDown += OnFindKeyDown;
        UpdateFindCount();

        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => RaiseFilterChanged();
        _filterBox.TextChanged += (_, _) =>
        {
            _timer.Stop();
            _timer.Start();
        };
        _filterBox.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape && !string.IsNullOrEmpty(_filterBox.Text))
            {
                e.Handled = true;
                _filterBox.Text = "";
            }
        };
        UpdateOfflineMode();
    }

    /// Filter text or cached toggle changed.
    public event Action? FilterChanged;

    public event Action? RefreshRequested;

    /// Page specific buttons (right aligned).
    public StackPanel Buttons { get; }

    public string FilterText => _filterBox.Text ?? "";

    public bool OnlyCached => _cachedToggle.IsChecked == true || AppServices.Instance.Settings.User.IsOfflineMode;

    public string FilterPlaceholder
    {
        set => _filterBox.PlaceholderText = value;
    }

    public bool IsCachedToggleVisible
    {
        set => _cachedToggle.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool IsRefreshVisible
    {
        set => _refreshButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool IsFilterVisible
    {
        set => _filterBox.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public void UpdateOfflineMode()
    {
        var offline = AppServices.Instance.Settings.User.IsOfflineMode;
        if (offline) _cachedToggle.IsChecked = true;
        _cachedToggle.IsEnabled = !offline;
        _refreshButton.IsEnabled = !offline;
    }

    /// Enables the "Jump to" search for the list of the controller.
    public ListFinder AttachFinder(LibraryListController controller)
    {
        _finder = new ListFinder(controller);
        _finder.Changed += UpdateFindCount;
        _findPanel.Visibility = Visibility.Visible;
        return _finder;
    }

    public ListFinder? Finder => _finder;

    /// The toolbar with a "Jump to" search on a page (null if the page has none).
    public static DetailListToolbar? FindIn(DependencyObject? root)
    {
        if (root is null) return null;
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is DetailListToolbar { _finder: not null } toolbar) return toolbar;
            if (FindIn(child) is { } found) return found;
        }
        return null;
    }

    /// Enters a "Jump to" text and jumps to the first match (E2E tour, automation).
    public void JumpTo(string text)
    {
        if (_finder is null) return;
        _findBox.Text = text;
        _findTimer.Stop();
        _finder.Search(text);
    }

    public void FocusFind()
    {
        if (_finder is null)
        {
            FocusFilter();
            return;
        }
        _findBox.Focus(FocusState.Keyboard);
        _findBox.SelectAll();
    }

    private void OnFindKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (_finder is null) return;
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        switch (e.Key)
        {
            case VirtualKey.Enter:
            case VirtualKey.F3:
                e.Handled = true;
                // apply typed text immediately (the debounce timer may not have fired yet)
                if (_findTimer.IsRunning)
                {
                    _findTimer.Stop();
                    _finder.Search(_findBox.Text ?? "");
                    if (!shift) break;
                }
                if (shift) _finder.Previous();
                else _finder.Next();
                break;
            case VirtualKey.Down:
                e.Handled = true;
                _finder.Next();
                break;
            case VirtualKey.Up:
                e.Handled = true;
                _finder.Previous();
                break;
            case VirtualKey.Escape:
                e.Handled = true;
                // go to the match (Enter plays it, the menu key opens its menu); otherwise clear
                if (!_finder.FocusCurrent()) _findBox.Text = "";
                break;
        }
    }

    private void UpdateFindCount()
    {
        var hasText = !string.IsNullOrWhiteSpace(_findBox.Text);
        var count = _finder?.MatchCount ?? 0;
        var current = _finder?.CurrentIndex ?? -1;
        _findCount.Text = !hasText ? "" : count == 0 ? "No match" : current < 0 ? (count == 1 ? "1 match" : $"{count} matches") : $"{current + 1} of {count}";
        _findPrevious.IsEnabled = _findNext.IsEnabled = count > 1;
    }

    public void FocusFilter()
    {
        if (_filterBox.Visibility != Visibility.Visible)
        {
            FocusFind();
            return;
        }
        _filterBox.Focus(FocusState.Keyboard);
        _filterBox.SelectAll();
    }

    private void RaiseFilterChanged()
    {
        var text = _filterBox.Text ?? "";
        if (text == _lastFilter) return;
        _lastFilter = text;
        FilterChanged?.Invoke();
    }

    /// Ctrl+F (jump to), Ctrl+Shift+F (filter), F3 / Shift+F3 (next / previous match) and F5 (refresh) on the page.
    public void AttachAccelerators(UIElement page)
    {
        var find = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = VirtualKey.F, Modifiers = VirtualKeyModifiers.Control };
        find.Invoked += (_, e) =>
        {
            e.Handled = true;
            FocusFind();
        };
        page.KeyboardAccelerators.Add(find);
        var filter = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = VirtualKey.F, Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift };
        filter.Invoked += (_, e) =>
        {
            e.Handled = true;
            FocusFilter();
        };
        page.KeyboardAccelerators.Add(filter);
        foreach (var (modifiers, next) in new[] { (VirtualKeyModifiers.None, true), (VirtualKeyModifiers.Shift, false) })
        {
            var f3 = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = VirtualKey.F3, Modifiers = modifiers };
            f3.Invoked += (_, e) =>
            {
                if (_finder is null) return;
                e.Handled = true;
                if (next) _finder.Next();
                else _finder.Previous();
            };
            page.KeyboardAccelerators.Add(f3);
        }
        var refresh = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = VirtualKey.F5 };
        refresh.Invoked += (_, e) =>
        {
            e.Handled = true;
            RefreshRequested?.Invoke();
        };
        page.KeyboardAccelerators.Add(refresh);
    }
}
