using Amperfy.App.Helpers;
using Amperfy.App.Services;
using Amperfy.Core.Common;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace Amperfy.App.Pages;

/// Main app shell (port of SplitVC + SideBarVC): sidebar with Search, Home and the library
/// categories of the active account, the content frame, an optional side pane (queue / lyrics)
/// and the player bar.
public sealed partial class ShellPage : Page
{
    private const string SearchTag = "search";
    private const string HomeTag = "home";
    private const string LibraryTagPrefix = "lib:";

    private readonly AppServices _services = AppServices.Instance;
    private bool _isSyncingSelection;
    private readonly List<IDisposable> _subscriptions = [];

    public static ShellPage? Current { get; private set; }

    public ShellPage()
    {
        InitializeComponent();
        Current = this;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var window = _services.MainWindow;
        window.PaneToggleRequested += Window_PaneToggleRequested;
        window.SearchRequested += Window_SearchRequested;
        _services.Navigation.Attach(ContentFrame);
        _services.Navigation.Navigated += Navigation_Navigated;
        _subscriptions.Add(_services.Notifications.Register(AmperfyNotification.AccountActiveChanged, _ => RebuildMenu()));
        _subscriptions.Add(_services.Notifications.Register(AmperfyNotification.LibraryDisplaySettingsChanged, _ => RebuildMenu()));
        RebuildMenu();
        _services.Kit.StartManagersForNormalOperation();
        _services.Navigation.Navigate(typeof(HomePage), clearBackStack: true);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        var window = _services.MainWindow;
        window.PaneToggleRequested -= Window_PaneToggleRequested;
        window.SearchRequested -= Window_SearchRequested;
        _services.Navigation.Navigated -= Navigation_Navigated;
        foreach (var s in _subscriptions) s.Dispose();
        _subscriptions.Clear();
        if (Current == this) Current = null;
    }

    private static NavigationViewItem CreateItem(string text, string glyph, string tag) => new()
    {
        Content = text,
        Icon = new FontIcon { Glyph = glyph },
        Tag = tag,
    };

    private void RebuildMenu()
    {
        _isSyncingSelection = true;
        NavView.MenuItems.Clear();
        NavView.MenuItems.Add(CreateItem("Search", Icons.Search, SearchTag));
        NavView.MenuItems.Add(CreateItem("Home", Icons.Home, HomeTag));
        NavView.MenuItems.Add(new NavigationViewItemHeader { Content = "Library" });
        var displaySettings = _services.Settings.Accounts.ActiveSetting.LibraryDisplaySettings;
        foreach (var type in displaySettings.InUse)
        {
            NavView.MenuItems.Add(CreateItem(type.DisplayName(), Icons.For(type), LibraryTagPrefix + type));
        }
        var account = _services.ActiveAccount;
        AccountItem.Content = account is null ? "Account" : $"{account.UserName} · {account.ServerUrl}";
        ToolTipService.SetToolTip(AccountItem, account is null ? null : $"{account.UserName}\n{account.ServerUrl}");
        _isSyncingSelection = false;
        SyncSelectionWithPage();
    }

    /// Navigates to the page of a sidebar entry.
    private void NavigateTo(string tag)
    {
        switch (tag)
        {
            case SearchTag:
                _services.Navigation.Navigate(typeof(SearchPage), _services.MainWindow.SearchBoxControl.Text);
                break;
            case HomeTag:
                _services.Navigation.Navigate(typeof(HomePage));
                break;
            default:
                if (tag.StartsWith(LibraryTagPrefix, StringComparison.Ordinal) &&
                    Enum.TryParse<LibraryDisplayType>(tag[LibraryTagPrefix.Length..], out var type))
                {
                    var (page, parameter) = PageRegistry.ForLibraryType(type);
                    _services.Navigation.Navigate(page, parameter);
                }
                break;
        }
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            _services.Navigation.Navigate(typeof(SettingsPage));
            return;
        }
        if (args.InvokedItemContainer?.Tag is string tag) NavigateTo(tag);
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // Navigation is done in ItemInvoked (also fires for re-selecting the current item).
    }

    private void Navigation_Navigated()
    {
        _services.MainWindow.UpdateBackButton(_services.Navigation.CanGoBack);
        SyncSelectionWithPage();
    }

    /// Highlights the sidebar entry belonging to the currently displayed page.
    private void SyncSelectionWithPage()
    {
        if (_isSyncingSelection) return;
        var pageType = ContentFrame.CurrentSourcePageType;
        string? tag = null;
        if (pageType == typeof(SearchPage)) tag = SearchTag;
        else if (pageType == typeof(HomePage)) tag = HomeTag;
        else if (pageType == typeof(SettingsPage))
        {
            NavView.SelectedItem = NavView.SettingsItem;
            return;
        }
        else if (PageRegistry.LibraryTypeOf(ContentFrame) is { } libType) tag = LibraryTagPrefix + libType;
        _isSyncingSelection = true;
        NavView.SelectedItem = tag is null ? null : NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string?)i.Tag == tag);
        _isSyncingSelection = false;
    }

    private void Window_PaneToggleRequested() => NavView.IsPaneOpen = !NavView.IsPaneOpen;

    private void Window_SearchRequested(string text, bool isSubmitted)
    {
        if (ContentFrame.Content is SearchPage searchPage)
        {
            searchPage.UpdateSearch(text, isSubmitted);
        }
        else if (!string.IsNullOrEmpty(text) || isSubmitted)
        {
            _services.Navigation.Navigate(typeof(SearchPage), text);
        }
    }

    /// Shows content (queue, lyrics, …) in the right side pane; null hides the pane.
    public void ShowSidePane(UIElement? content)
    {
        SidePaneHost.Child = content;
        SidePaneHost.Visibility = content is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public UIElement? SidePaneContent => SidePaneHost.Child;

    private void AccountItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        var active = _services.Settings.Accounts.Active;
        foreach (var info in _services.Settings.Accounts.AllAccounts)
        {
            var account = _services.Library.GetAccount(info);
            var item = new ToggleMenuFlyoutItem
            {
                Text = $"{account.UserName} · {account.ServerUrl}",
                IsChecked = info == active,
            };
            item.Click += (_, _) =>
            {
                if (info == _services.Settings.Accounts.Active) return;
                _services.Kit.SwitchActiveAccount(info);
                _services.Navigation.Navigate(typeof(HomePage), clearBackStack: true);
            };
            flyout.Items.Add(item);
        }
        flyout.Items.Add(new MenuFlyoutSeparator());
        var add = new MenuFlyoutItem { Text = "Add account…", Icon = new FontIcon { Glyph = Icons.Add } };
        add.Click += (_, _) => _services.MainWindow.ShowLogin(isAddingAccount: true);
        flyout.Items.Add(add);
        var settings = new MenuFlyoutItem { Text = "Account settings", Icon = new FontIcon { Glyph = Icons.Settings } };
        settings.Click += (_, _) => _services.Navigation.Navigate(typeof(SettingsPage), SettingsPage.AccountSection);
        flyout.Items.Add(settings);
        flyout.ShowAt(AccountItem);
    }
}
