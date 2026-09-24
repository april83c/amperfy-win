using Amperfy.App.Services;
using Amperfy.Core.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace Amperfy.App.Pages;

/// Server login (port of LoginVC). Navigation parameter "addAccount" (bool) shows a cancel button
/// to return to the app when adding a further account.
public sealed partial class LoginPage : Page
{
    private readonly AppServices _services = AppServices.Instance;
    private bool _isAddingAccount;

    public LoginPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _isAddingAccount = e.Parameter is true;
        CancelButton.Visibility = _isAddingAccount ? Visibility.Visible : Visibility.Collapsed;
        HeaderText.Text = _isAddingAccount ? "Add Account" : "Amperfy";
        ServerUrlBox.Focus(FocusState.Programmatic);
    }

    private BackendApiType SelectedApiType => ApiTypeBox.SelectedIndex switch
    {
        1 => BackendApiType.Ampache,
        2 => BackendApiType.Subsonic,
        3 => BackendApiType.SubsonicLegacy,
        _ => BackendApiType.NotDetected,
    };

    public static Dictionary<string, string> ParseHeaders(string text)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            var name = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            if (name.Length > 0) headers[name] = value;
        }
        return headers;
    }

    private void Input_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            _ = LoginAsync();
        }
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e) => _ = LoginAsync();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _services.MainWindow.ShowShell();

    private void SetBusy(bool busy)
    {
        LoginButton.IsEnabled = !busy;
        LoginProgress.IsActive = busy;
        ServerUrlBox.IsEnabled = UsernameBox.IsEnabled = PasswordBox.IsEnabled = ApiTypeBox.IsEnabled = HeadersBox.IsEnabled = !busy;
    }

    private async Task LoginAsync()
    {
        if (!LoginButton.IsEnabled) return;
        ErrorBar.IsOpen = false;
        SetBusy(true);
        try
        {
            var headers = ParseHeaders(HeadersBox.Text);
            var account = await _services.Kit.LoginAsync(ServerUrlBox.Text, UsernameBox.Text, PasswordBox.Password, SelectedApiType,
                headers.Count > 0 ? headers : null);
            _services.MainWindow.ShowSync(account);
        }
        catch (Exception ex)
        {
            ErrorBar.Title = "Login failed";
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
        }
        finally
        {
            SetBusy(false);
        }
    }
}
