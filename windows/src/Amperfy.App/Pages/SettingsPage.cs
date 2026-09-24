using Amperfy.App.Services;
using Amperfy.Core.Model;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Amperfy.App.Pages;

/// Placeholder (to be implemented).
public sealed partial class SettingsPage : Page
{
    private readonly TextBlock Header = new() { Text = "Settings", Margin = new Thickness(24), Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] };

    public const string AccountSection = "account";

    public SettingsPage()
    {
        Content = Header;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
    }
}
