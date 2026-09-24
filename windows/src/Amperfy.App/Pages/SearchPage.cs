using Amperfy.App.Services;
using Amperfy.Core.Model;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Amperfy.App.Pages;

/// Placeholder (to be implemented).
public sealed partial class SearchPage : Page
{
    private readonly TextBlock Header = new() { Text = "Search", Margin = new Thickness(24), Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] };

    public void UpdateSearch(string text, bool isSubmitted) => Header.Text = $"Search: {text}";

    public SearchPage()
    {
        Content = Header;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string s) UpdateSearch(s, false);
    }
}
