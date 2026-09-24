using Amperfy.App.Pages;
using Amperfy.App.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Amperfy.App;

public sealed partial class MainWindow : Window
{
    private readonly AppServices _services = AppServices.Instance;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        RestoreWindowPlacement();
        AppWindow.Closing += (_, _) => SaveWindowPlacement();
        RootGrid.RequestedTheme = _services.RequestedElementTheme;
        _services.Alerts.Attach(AlertHost);
        RootGrid.Loaded += (_, _) =>
        {
            _services.Dialogs.Attach(RootGrid.XamlRoot);
            ShowStartPage();
            E2ETour.TryStart(this);
        };
    }

    public Frame RootContentFrame => RootFrame;
    public TitleBar TitleBarControl => AppTitleBar;
    public AutoSuggestBox SearchBoxControl => SearchBox;

    /// Search text changes / submissions (handled by the shell).
    public event Action<string, bool>? SearchRequested;
    public event Action? PaneToggleRequested;

    public void ShowStartPage()
    {
        if (!_services.Kit.IsLoggedIn) ShowLogin();
        else ShowShell();
    }

    public void ShowLogin(bool isAddingAccount = false)
    {
        SetShellChrome(false);
        RootFrame.Navigate(typeof(LoginPage), isAddingAccount);
    }

    public void ShowSync(Amperfy.Core.Model.Account account)
    {
        SetShellChrome(false);
        RootFrame.Navigate(typeof(SyncPage), account);
    }

    public void ShowShell()
    {
        SetShellChrome(true);
        RootFrame.Navigate(typeof(ShellPage));
        RootFrame.BackStack.Clear();
    }

    private void SetShellChrome(bool isShell)
    {
        SearchBox.Visibility = isShell ? Visibility.Visible : Visibility.Collapsed;
        AppTitleBar.IsPaneToggleButtonVisible = isShell;
        if (!isShell) AppTitleBar.IsBackButtonVisible = false;
    }

    public void UpdateBackButton(bool canGoBack) => AppTitleBar.IsBackButtonVisible = canGoBack;

    /// Applies the appearance mode (light / dark / system) to the window content (settings).
    public void ApplyRequestedTheme(ElementTheme theme) => RootGrid.RequestedTheme = theme;

    /// Re-evaluates the theme resources of the window content, e.g. after the accent color changed
    /// (toggles the requested theme once).
    public void RefreshThemeResources()
    {
        var requested = RootGrid.RequestedTheme;
        RootGrid.RequestedTheme = RootGrid.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        RootGrid.RequestedTheme = requested;
    }

    private void AppTitleBar_BackRequested(TitleBar sender, object args) => _services.Navigation.GoBack();

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args) => PaneToggleRequested?.Invoke();

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) SearchRequested?.Invoke(sender.Text, false);
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        SearchRequested?.Invoke(args.QueryText, true);

    private void RestoreWindowPlacement()
    {
        var bounds = _services.Settings.App.MainWindowBounds;
        if (bounds is { Length: 4 } && bounds[2] >= 640 && bounds[3] >= 480)
        {
            AppWindow.MoveAndResize(new RectInt32(bounds[0], bounds[1], bounds[2], bounds[3]));
        }
        else
        {
            AppWindow.Resize(new SizeInt32(1280, 820));
        }
        if (_services.Settings.App.IsMainWindowMaximized && AppWindow.Presenter is OverlappedPresenter p) p.Maximize();
    }

    private void SaveWindowPlacement()
    {
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            _services.Settings.App.IsMainWindowMaximized = p.State == OverlappedPresenterState.Maximized;
            if (p.State == OverlappedPresenterState.Restored)
            {
                var pos = AppWindow.Position;
                var size = AppWindow.Size;
                _services.Settings.App.MainWindowBounds = [pos.X, pos.Y, size.Width, size.Height];
            }
        }
        _services.Settings.SaveNow();
    }
}
