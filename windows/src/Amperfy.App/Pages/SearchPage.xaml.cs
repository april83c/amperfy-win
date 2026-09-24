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

    public void UpdateSearch(string text, bool isSubmitted) => Header.Text = $"Search: {text}";

    public SearchPage()
    {
        InitializeComponent();
        Header.Text = "Search";
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string s) UpdateSearch(s, false);
    }
}
