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

    public const string AccountSection = "account";

    public SettingsPage()
    {
        InitializeComponent();
        Header.Text = "Settings";
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
    }
}
