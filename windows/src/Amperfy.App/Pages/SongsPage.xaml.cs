using Amperfy.App.Services;
using Amperfy.Core.Model;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Amperfy.App.Pages;

/// Placeholder (to be implemented).
public sealed partial class SongsPage : Page, ILibraryCategoryPage
{

    public LibraryDisplayType LibraryType { get; private set; }

    public SongsPage()
    {
        InitializeComponent();
        Header.Text = "Songs";
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is LibraryDisplayType t) LibraryType = t;
        Header.Text = LibraryType.DisplayName();
    }
}
