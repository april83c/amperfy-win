using Amperfy.App.Services;
using Amperfy.Core.Model;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Amperfy.App.Pages;

/// Placeholder (to be implemented).
public sealed partial class AlbumDetailPage : Page
{

    public AlbumDetailPage()
    {
        InitializeComponent();
        Header.Text = "Album";
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is AbstractLibraryEntity entity) Header.Text = $"Album {entity.Pk}";
    }
}
