using Amperfy.App.Helpers;
using Amperfy.App.Services;
using Amperfy.Core.Common;
using Amperfy.Core.Model;
using Amperfy.Core.Storage;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Amperfy.App.Pages.Settings;

/// Account settings (port of AccountSettingsView, UpdatePasswordView, ServerURLsSettingsView,
/// AlternativeURLAddDialogView and CustomHTTPHeadersView) plus the account list (switch / add).
public sealed partial class AccountSettingsPage : Page
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly List<IDisposable> _subscriptions = [];
    private bool _isLoading;

    private static readonly ThemePreference[] ThemeValues = Enum.GetValues<ThemePreference>();
    private static readonly ArtworkDisplayPreference[] ArtworkDisplayValues =
    [
        ArtworkDisplayPreference.PreferId3Tag, ArtworkDisplayPreference.PreferServerArtwork,
        ArtworkDisplayPreference.ServerArtworkOnly, ArtworkDisplayPreference.Id3TagOnly,
    ];

    public AccountSettingsPage()
    {
        InitializeComponent();
        foreach (var theme in ThemeValues)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(new Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush(theme.AccentColor()), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = theme.Description() });
            ThemeCombo.Items.Add(new ComboBoxItem { Content = row, Tag = theme });
        }
        foreach (var preference in ArtworkDisplayValues) ArtworkDisplayCombo.Items.Add(preference.Description());

        ThemeCombo.SelectionChanged += ThemeCombo_SelectionChanged;
        ArtworkDisplayCombo.SelectionChanged += ArtworkDisplayCombo_SelectionChanged;
        AddAccountButton.Click += (_, _) => _services.MainWindow.ShowLogin(isAddingAccount: true);
        SidebarCard.Click += (_, _) => SettingsPage.Open(Frame, SettingsPage.SidebarSection);
        AddUrlButton.Click += (_, _) => _ = AddAlternativeUrlAsync();
        EditHeadersButton.Click += (_, _) => _ = EditHeadersAsync();
        UpdatePasswordButton.Click += (_, _) => _ = UpdatePasswordAsync();
        ResyncButton.Click += (_, _) => _ = ResyncLibraryAsync();
        LogoutButton.Click += (_, _) => _ = LogoutAsync();

        Loaded += (_, _) =>
        {
            if (_subscriptions.Count == 0)
                _subscriptions.Add(_services.Notifications.Register(AmperfyNotification.AccountActiveChanged, _ => Reload()));
            Reload();
        };
        Unloaded += (_, _) =>
        {
            foreach (var s in _subscriptions) s.Dispose();
            _subscriptions.Clear();
        };
    }

    private AccountInfo? ActiveInfo => _services.Settings.Accounts.Active;

    private LoginCredentials? Credentials(AccountInfo info) => _services.Settings.Accounts.GetSetting(info).LoginCredentials;

    private void Reload()
    {
        _isLoading = true;
        try
        {
            ReloadAccountList();
            var info = ActiveInfo;
            var credentials = info is null ? null : Credentials(info);
            NotLoggedInText.Visibility = credentials is null ? Visibility.Visible : Visibility.Collapsed;
            ActiveAccountPanel.Visibility = credentials is null ? Visibility.Collapsed : Visibility.Visible;
            if (info is null || credentials is null) return;

            var setting = _services.Settings.Accounts.GetSetting(info);
            ActiveAccountHeader.Text = $"Active account: {credentials.Username}";
            ServerExpander.Description = credentials.DisplayServerUrl;
            ServerUrlText.Text = credentials.ServerUrl;
            UsernameText.Text = credentials.Username;
            BackendApiText.Text = credentials.BackendApi.Description();
            var meta = _services.Kit.GetMeta(info);
            ServerApiVersionText.Text = SafeGet(() => meta.BackendApi.ServerApiVersion);
            ClientApiVersionText.Text = SafeGet(() => meta.BackendApi.ClientApiVersion);

            ThemeCombo.SelectedIndex = Array.IndexOf(ThemeValues, setting.ThemePreference);
            ArtworkDisplayCombo.SelectedIndex = Array.IndexOf(ArtworkDisplayValues, setting.ArtworkDisplayPreference);

            ReloadServerUrls(credentials);
            HeadersCard.Description = credentials.HttpHeaders.Count == 0
                ? "Headers sent with every request, e.g. for a reverse proxy or an access token. None configured."
                : $"Headers sent with every request: {string.Join(", ", credentials.HttpHeaders.Keys)}";
        }
        finally
        {
            _isLoading = false;
        }
    }

    private static string SafeGet(Func<string> get)
    {
        try
        {
            var value = get();
            return string.IsNullOrEmpty(value) ? "–" : value;
        }
        catch
        {
            return "–";
        }
    }

    // --- account list --------------------------------------------------------------------------

    private void ReloadAccountList()
    {
        AccountsExpander.Items.Clear();
        var active = ActiveInfo;
        foreach (var info in _services.Settings.Accounts.AllAccounts)
        {
            var setting = _services.Settings.Accounts.GetSetting(info);
            var credentials = setting.LoginCredentials;
            var isActive = info == active;
            var card = new SettingsCard
            {
                Header = credentials?.Username ?? info.Ident,
                Description = credentials is null ? "" : $"{credentials.DisplayServerUrl} · {credentials.BackendApi.Description()}",
                HeaderIcon = new FontIcon { Glyph = SettingsGlyphs.Account, Foreground = new SolidColorBrush(setting.ThemePreference.AccentColor()) },
            };
            if (isActive)
            {
                var activeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                activeRow.Children.Add(new FontIcon { Glyph = SettingsGlyphs.Checkmark, FontSize = 14 });
                activeRow.Children.Add(new TextBlock { Text = "Active" });
                card.Content = activeRow;
            }
            else
            {
                card.Content = SettingsUi.Button("Switch", () => SwitchAccount(info));
            }
            ToolTipService.SetToolTip(card, credentials?.ServerUrl);
            AccountsExpander.Items.Add(card);
        }
    }

    private void SwitchAccount(AccountInfo info)
    {
        if (info == ActiveInfo) return;
        _services.Kit.SwitchActiveAccount(info);
        // AccountActiveChanged: the shell rebuilds the sidebar, the theme service applies the accent, this page reloads
    }

    // --- appearance ----------------------------------------------------------------------------

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || ActiveInfo is not { } info || ThemeCombo.SelectedItem is not ComboBoxItem { Tag: ThemePreference theme }) return;
        if (_services.Settings.Accounts.GetSetting(info).ThemePreference == theme) return;
        _services.Settings.Accounts.UpdateSetting(info, s => s.ThemePreference = theme);
        ThemeService.ApplyAccentColor(theme);
        ReloadAccountList();
    }

    private void ArtworkDisplayCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || ActiveInfo is not { } info || ArtworkDisplayCombo.SelectedIndex < 0) return;
        var preference = ArtworkDisplayValues[ArtworkDisplayCombo.SelectedIndex];
        _services.Settings.Accounts.UpdateSetting(info, s => s.ArtworkDisplayPreference = preference);
    }

    // --- server URLs ---------------------------------------------------------------------------

    private void ReloadServerUrls(LoginCredentials credentials)
    {
        ServerUrlsExpander.Items.Clear();
        var activeUrl = string.IsNullOrEmpty(credentials.ActiveBackendServerUrl) ? credentials.ServerUrl : credentials.ActiveBackendServerUrl;
        foreach (var url in credentials.AvailableServerURLs.Distinct())
        {
            var isActive = url == activeUrl;
            var isAccountUrl = url == credentials.ServerUrl;
            var card = new SettingsCard
            {
                Header = url,
                Description = (isActive, isAccountUrl) switch
                {
                    (true, true) => "Account URL · In use",
                    (true, false) => "Alternative URL · In use",
                    (false, true) => "Account URL",
                    _ => "Alternative URL",
                },
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (!isActive) buttons.Children.Add(SettingsUi.Button("Use", () => SetActiveUrl(url)));
            if (!isActive && !isAccountUrl) buttons.Children.Add(SettingsUi.Button("Remove", () => DeleteUrl(url)));
            if (isActive) buttons.Children.Add(new FontIcon { Glyph = SettingsGlyphs.Checkmark, FontSize = 14 });
            card.Content = buttons;
            ServerUrlsExpander.Items.Add(card);
        }
        ServerUrlsExpander.Description = credentials.AlternativeServerURLs.Count == 0
            ? "Add alternative URLs (e.g. a local address) that reach the same server."
            : $"In use: {activeUrl}";
    }

    private void ProvideUpdatedCredentials(AccountInfo info)
    {
        if (Credentials(info) is { } updated) _services.Kit.GetMeta(info).BackendApi.ProvideCredentials(updated);
    }

    private void SetActiveUrl(string url)
    {
        if (ActiveInfo is not { } info || Credentials(info) is null) return;
        _services.Settings.Accounts.UpdateSetting(info, s => s.LoginCredentials!.ActiveBackendServerUrl = url);
        ProvideUpdatedCredentials(info);
        Reload();
    }

    private void DeleteUrl(string url)
    {
        if (ActiveInfo is not { } info || Credentials(info) is not { } credentials) return;
        var activeUrl = string.IsNullOrEmpty(credentials.ActiveBackendServerUrl) ? credentials.ServerUrl : credentials.ActiveBackendServerUrl;
        if (url == activeUrl || url == credentials.ServerUrl) return;
        _services.Settings.Accounts.UpdateSetting(info, s => s.LoginCredentials!.AlternativeServerURLs.Remove(url));
        Reload();
    }

    private async Task AddAlternativeUrlAsync()
    {
        if (ActiveInfo is not { } info || Credentials(info) is not { } credentials) return;
        var urlBox = new TextBox { Header = "URL", PlaceholderText = "https://192.168.1.10:4533" };
        var userBox = new TextBox { Header = "Username", Text = credentials.Username, IsEnabled = false };
        var passwordBox = new PasswordBox { Header = "Password" };
        var progress = new ProgressBar { IsIndeterminate = true, Visibility = Visibility.Collapsed };
        var status = new InfoBar { IsClosable = false, IsOpen = false, Severity = InfoBarSeverity.Error };
        var panel = new StackPanel { Spacing = 12, MinWidth = 360 };
        panel.Children.Add(new TextBlock { Text = "The URL must reach the same server. Otherwise library inconsistencies will occur.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(urlBox);
        panel.Children.Add(userBox);
        panel.Children.Add(passwordBox);
        panel.Children.Add(progress);
        panel.Children.Add(status);
        var dialog = new ContentDialog
        {
            Title = "Add alternative URL",
            Content = panel,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        void ShowError(string message)
        {
            status.Message = message;
            status.IsOpen = true;
        }
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                status.IsOpen = false;
                var newUrl = urlBox.Text.Trim();
                if (string.IsNullOrEmpty(newUrl) || string.IsNullOrEmpty(passwordBox.Password))
                {
                    ShowError("Inputs are not valid.");
                    args.Cancel = true;
                    return;
                }
                if (credentials.AvailableServerURLs.Contains(newUrl))
                {
                    ShowError("Provided URL is already in URLs list.");
                    args.Cancel = true;
                    return;
                }
                if (!newUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !newUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    ShowError("Please provide either 'https://' or 'http://' in your server URL.");
                    args.Cancel = true;
                    return;
                }
                var toCheck = credentials.Clone();
                toCheck.Password = passwordBox.Password;
                toCheck.ActiveBackendServerUrl = newUrl;
                toCheck.AlternativeServerURLs.Add(newUrl);
                progress.Visibility = Visibility.Visible;
                dialog.IsPrimaryButtonEnabled = false;
                try
                {
                    await _services.Kit.GetMeta(info).BackendApi.IsAuthenticationValidAsync(toCheck);
                }
                catch (Exception ex)
                {
                    ShowError($"Alternative URL could not be verified! Authentication failed! Alternative URL has not been added.\n{ex.Message}");
                    args.Cancel = true;
                    return;
                }
                _services.Settings.Accounts.UpdateSetting(info, s => s.LoginCredentials = toCheck);
                ProvideUpdatedCredentials(info);
                _services.Alerts.ShowInfo("Server URLs", "Alternative URL added.");
            }
            finally
            {
                progress.Visibility = Visibility.Collapsed;
                dialog.IsPrimaryButtonEnabled = true;
                deferral.Complete();
            }
        };
        await this.ShowDialogAsync(dialog);
        Reload();
    }

    // --- HTTP headers --------------------------------------------------------------------------

    private async Task EditHeadersAsync()
    {
        if (ActiveInfo is not { } info || Credentials(info) is not { } credentials) return;
        var box = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinHeight = 140,
            MinWidth = 400,
            PlaceholderText = "X-Api-Key: secret",
            Text = string.Join("\r", credentials.HttpHeaders.Select(h => $"{h.Key}: {h.Value}")),
        };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "Headers sent with every request to the server (e.g. for a reverse proxy or a Cloudflare Access service token). One per line in the form Name: Value",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(box);
        var dialog = new ContentDialog
        {
            Title = "Custom HTTP Headers",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await this.ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
        var headers = LoginPage.ParseHeaders(box.Text.Replace("\r\n", "\n").Replace('\r', '\n'));
        _services.Settings.Accounts.UpdateSetting(info, s => s.LoginCredentials!.HttpHeaders = headers);
        ProvideUpdatedCredentials(info);
        Reload();
    }

    // --- password ------------------------------------------------------------------------------

    private async Task UpdatePasswordAsync()
    {
        if (ActiveInfo is not { } info || Credentials(info) is not { } credentials) return;
        var passwordBox = new PasswordBox { Header = "New password", PlaceholderText = "Change account password…" };
        var progress = new ProgressBar { IsIndeterminate = true, Visibility = Visibility.Collapsed };
        var status = new InfoBar { IsClosable = false, IsOpen = false, Severity = InfoBarSeverity.Error };
        var panel = new StackPanel { Spacing = 12, MinWidth = 360 };
        panel.Children.Add(new TextBlock { Text = $"{credentials.Username} · {credentials.DisplayServerUrl}", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(passwordBox);
        panel.Children.Add(progress);
        panel.Children.Add(status);
        var dialog = new ContentDialog
        {
            Title = "Update Password",
            Content = panel,
            PrimaryButtonText = "Update",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        passwordBox.Loaded += (_, _) => passwordBox.Focus(FocusState.Programmatic);
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                status.IsOpen = false;
                if (string.IsNullOrEmpty(passwordBox.Password))
                {
                    status.Message = "Please provide the new password.";
                    status.IsOpen = true;
                    args.Cancel = true;
                    return;
                }
                var updated = credentials.Clone();
                updated.Password = passwordBox.Password;
                progress.Visibility = Visibility.Visible;
                dialog.IsPrimaryButtonEnabled = false;
                try
                {
                    await _services.Kit.GetMeta(info).BackendApi.IsAuthenticationValidAsync(updated);
                }
                catch
                {
                    status.Message = "Authentication failed! Password has not been updated.";
                    status.IsOpen = true;
                    args.Cancel = true;
                    return;
                }
                _services.Settings.Accounts.UpdateSetting(info, s => s.LoginCredentials = updated);
                ProvideUpdatedCredentials(info);
                _services.Alerts.ShowInfo("Password", "Password updated!");
            }
            finally
            {
                progress.Visibility = Visibility.Collapsed;
                dialog.IsPrimaryButtonEnabled = true;
                deferral.Complete();
            }
        };
        await this.ShowDialogAsync(dialog);
    }

    // --- resync / logout -----------------------------------------------------------------------

    /// Port of AccountSettingsView.resyncLibrary: stops the account services and shows the sync page.
    private async Task ResyncLibraryAsync()
    {
        if (ActiveInfo is not { } info) return;
        var confirmed = await _services.Dialogs.ConfirmAsync("Resync Library",
            "This will reset your local library and start syncing again from the server. Your downloaded files will remain on this device.\n\nDo you want to resync your library?",
            "Resync", destructive: true);
        if (!confirmed) return;
        try
        {
            _services.Kit.GetMeta(info).StopManager();
            _services.Kit.ResetMeta(info);
            _services.Settings.User.IsOfflineMode = false;
            var account = _services.Library.GetAccount(info);
            _services.Player.Logout(account);
            _services.MainWindow.ShowSync(account);
        }
        catch (Exception ex)
        {
            _services.EventLogger.Report("Resync Library", ex);
        }
    }

    /// Port of AccountSettingsView.logout.
    private async Task LogoutAsync()
    {
        if (ActiveInfo is not { } info) return;
        var confirmed = await _services.Dialogs.ConfirmAsync("Logout",
            "Logging out will sign you out of the current account. Your login credentials will be removed, and all downloaded files for this account will be deleted.\n\nDo you want to log out?",
            "Logout", destructive: true);
        if (!confirmed) return;
        try
        {
            _services.Kit.Logout(info);
        }
        catch (Exception ex)
        {
            _services.EventLogger.Report("Logout", ex);
        }
        if (_services.Settings.Accounts.Active is null)
        {
            // no other account: behave like the initial app start (force resync after login)
            _services.Settings.App.IsLibrarySynced = false;
            _services.MainWindow.ShowLogin();
        }
        else
        {
            _services.Navigation.Navigate(typeof(HomePage), clearBackStack: true);
        }
    }
}
