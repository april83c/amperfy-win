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

/// Toolbar below a detail header: in-page search, "Cached" filter (forced in offline mode),
/// refresh and page specific buttons (port of the detail pages' search controller + refresh control).
public sealed partial class DetailListToolbar : UserControl
{
    private readonly TextBox _filterBox;
    private readonly ToggleButton _cachedToggle;
    private readonly Button _refreshButton;
    private readonly DispatcherQueueTimer _timer;
    private string _lastFilter = "";

    public DetailListToolbar()
    {
        var root = new Grid { ColumnSpacing = 8, Padding = new Thickness(24, 0, 24, 8) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 360 });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _filterBox = new TextBox { PlaceholderText = "Search", VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(_filterBox, "Filter (Ctrl+F)");
        root.Children.Add(_filterBox);

        var cachedContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        cachedContent.Children.Add(Ui.Icon(LibraryGlyphs.Cached, 14));
        cachedContent.Children.Add(new TextBlock { Text = "Cached", VerticalAlignment = VerticalAlignment.Center });
        _cachedToggle = new ToggleButton { Content = cachedContent, VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(_cachedToggle, "Show only cached items");
        _cachedToggle.Click += (_, _) => FilterChanged?.Invoke();
        Grid.SetColumn(_cachedToggle, 1);
        root.Children.Add(_cachedToggle);

        _refreshButton = Ui.IconButton(Icons.Refresh, "Refresh (F5)", (_, _) => RefreshRequested?.Invoke());
        Grid.SetColumn(_refreshButton, 2);
        root.Children.Add(_refreshButton);

        Buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(Buttons, 3);
        root.Children.Add(Buttons);
        Content = root;

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

    public void FocusFilter()
    {
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

    /// Ctrl+F (filter) and F5 (refresh) on the page.
    public void AttachAccelerators(UIElement page)
    {
        var find = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = VirtualKey.F, Modifiers = VirtualKeyModifiers.Control };
        find.Invoked += (_, e) =>
        {
            e.Handled = true;
            FocusFilter();
        };
        page.KeyboardAccelerators.Add(find);
        var refresh = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = VirtualKey.F5 };
        refresh.Invoked += (_, e) =>
        {
            e.Handled = true;
            RefreshRequested?.Invoke();
        };
        page.KeyboardAccelerators.Add(refresh);
    }
}
