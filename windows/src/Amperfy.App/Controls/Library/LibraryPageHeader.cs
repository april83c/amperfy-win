using Amperfy.App.Helpers;
using Amperfy.App.Library;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VirtualKey = Windows.System.VirtualKey;
using VirtualKeyModifiers = Windows.System.VirtualKeyModifiers;

namespace Amperfy.App.Controls;

/// Header of the library category pages (port of the navigation bar items + PlayShuffleInfo header +
/// search controller of the Swift table views): title, info ("123 Songs"), Play / Shuffle, an
/// in-page filter box and a command bar (sort, filter, view style, refresh, more).
public sealed partial class LibraryPageHeader : UserControl
{
    private readonly TextBlock _title;
    private readonly TextBlock _info;
    private readonly ProgressRing _busy;
    private readonly Button _playButton;
    private readonly Button _shuffleButton;
    private readonly TextBox _filterBox;
    private readonly DispatcherQueueTimer _filterTimer;
    private string _lastFilter = "";

    public LibraryPageHeader()
    {
        var root = new Grid { Padding = new Thickness(24, 12, 24, 8), RowSpacing = 8 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        _title = Ui.Text("", "TitleTextBlockStyle");
        _info = Ui.Text("", "BodyTextBlockStyle", secondary: true);
        _info.VerticalAlignment = VerticalAlignment.Bottom;
        _info.Margin = new Thickness(0, 0, 0, 4);
        _busy = new ProgressRing { Width = 18, Height = 18, IsActive = false, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        titleRow.Children.Add(_title);
        titleRow.Children.Add(_info);
        titleRow.Children.Add(_busy);
        root.Children.Add(titleRow);

        var controls = new Grid { ColumnSpacing = 8 };
        Grid.SetRow(controls, 1);
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _playButton = Ui.TextButton("Play", Icons.Play, (_, _) => PlayRequested?.Invoke(), accent: true, tooltip: "Play");
        _shuffleButton = Ui.TextButton("Shuffle", Icons.Shuffle, (_, _) => ShuffleRequested?.Invoke(), tooltip: "Shuffle");
        Grid.SetColumn(_shuffleButton, 1);
        controls.Children.Add(_playButton);
        controls.Children.Add(_shuffleButton);

        _filterBox = new TextBox { PlaceholderText = "Search", MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(_filterBox, "Filter (Ctrl+F)");
        Grid.SetColumn(_filterBox, 2);
        controls.Children.Add(_filterBox);

        Commands = new CommandBar
        {
            DefaultLabelPosition = CommandBarDefaultLabelPosition.Right,
            IsDynamicOverflowEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        Grid.SetColumn(Commands, 3);
        controls.Children.Add(Commands);
        root.Children.Add(controls);
        Content = root;

        _filterTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _filterTimer.Interval = TimeSpan.FromMilliseconds(300);
        _filterTimer.IsRepeating = false;
        _filterTimer.Tick += (_, _) => RaiseFilterChanged();
        _filterBox.TextChanged += (_, _) =>
        {
            _filterTimer.Stop();
            _filterTimer.Start();
        };
        _filterBox.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                _filterTimer.Stop();
                RaiseFilterChanged(force: true);
            }
            else if (e.Key == VirtualKey.Escape && !string.IsNullOrEmpty(_filterBox.Text))
            {
                e.Handled = true;
                _filterBox.Text = "";
            }
        };
    }

    public event Action? PlayRequested;
    public event Action? ShuffleRequested;

    /// Debounced filter text changes (also on Enter). The bool is true when submitted with Enter.
    public event Action<string, bool>? FilterChanged;

    public CommandBar Commands { get; }

    public string Title
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    public string Info
    {
        get => _info.Text;
        set => _info.Text = value;
    }

    public string FilterText => _filterBox.Text ?? "";

    public string FilterPlaceholder
    {
        set => _filterBox.PlaceholderText = value;
    }

    public bool IsFilterVisible
    {
        set => _filterBox.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool IsPlayVisible
    {
        set => _playButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool IsShuffleVisible
    {
        set => _shuffleButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool IsPlayEnabled
    {
        set
        {
            _playButton.IsEnabled = value;
            _shuffleButton.IsEnabled = value;
        }
    }

    public bool IsBusy
    {
        set
        {
            _busy.IsActive = value;
            _busy.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public void FocusFilter()
    {
        _filterBox.Focus(FocusState.Keyboard);
        _filterBox.SelectAll();
    }

    public void ClearFilter()
    {
        _filterBox.Text = "";
        _lastFilter = "";
    }

    private void RaiseFilterChanged(bool force = false)
    {
        var text = _filterBox.Text ?? "";
        if (!force && text == _lastFilter) return;
        _lastFilter = text;
        FilterChanged?.Invoke(text, force);
    }

    // --- command helpers -------------------------------------------------------------------------

    public AppBarButton AddCommand(string label, string glyph, Action onClick, string? tooltip = null,
        VirtualKey? key = null, VirtualKeyModifiers modifiers = VirtualKeyModifiers.None, bool secondary = false)
    {
        var button = new AppBarButton { Label = label, Icon = new FontIcon { Glyph = glyph } };
        ToolTipService.SetToolTip(button, tooltip ?? label);
        button.Click += (_, _) => onClick();
        if (key is { } k) button.KeyboardAccelerators.Add(new KeyboardAccelerator { Key = k, Modifiers = modifiers });
        if (secondary) Commands.SecondaryCommands.Add(button);
        else Commands.PrimaryCommands.Add(button);
        return button;
    }

    /// Button with a menu that is rebuilt when opened (e.g. sort options with the current check mark).
    public AppBarButton AddMenuCommand(string label, string glyph, Func<IEnumerable<MenuFlyoutItemBase>> buildItems, string? tooltip = null)
    {
        var flyout = new MenuFlyout();
        flyout.Opening += (_, _) =>
        {
            flyout.Items.Clear();
            foreach (var item in buildItems()) flyout.Items.Add(item);
            if (flyout.Items.Count == 0) flyout.Items.Add(new MenuFlyoutItem { Text = "No options", IsEnabled = false });
        };
        var button = new AppBarButton { Label = label, Icon = new FontIcon { Glyph = glyph }, Flyout = flyout };
        ToolTipService.SetToolTip(button, tooltip ?? label);
        Commands.PrimaryCommands.Add(button);
        return button;
    }

    public AppBarToggleButton AddToggleCommand(string label, string glyph, bool isChecked, Action<bool> onToggle, string? tooltip = null)
    {
        var button = new AppBarToggleButton { Label = label, Icon = new FontIcon { Glyph = glyph }, IsChecked = isChecked };
        ToolTipService.SetToolTip(button, tooltip ?? label);
        button.Click += (_, _) => onToggle(button.IsChecked == true);
        Commands.PrimaryCommands.Add(button);
        return button;
    }

    /// Adds Ctrl+F (focus filter) and F5 (refresh) accelerators to the page.
    public void AttachAccelerators(UIElement page, Action? refresh)
    {
        var find = new KeyboardAccelerator { Key = VirtualKey.F, Modifiers = VirtualKeyModifiers.Control };
        find.Invoked += (_, e) =>
        {
            e.Handled = true;
            FocusFilter();
        };
        page.KeyboardAccelerators.Add(find);
        if (refresh is not null)
        {
            var f5 = new KeyboardAccelerator { Key = VirtualKey.F5 };
            f5.Invoked += (_, e) =>
            {
                e.Handled = true;
                refresh();
            };
            page.KeyboardAccelerators.Add(f5);
        }
    }
}
