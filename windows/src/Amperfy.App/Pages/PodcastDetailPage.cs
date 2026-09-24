using Amperfy.App.Services;
using Amperfy.Core.Model;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Amperfy.App.Pages;

/// Placeholder (to be implemented).
public sealed partial class PodcastDetailPage : Page
{
    private readonly TextBlock Header = new() { Text = "Podcast", Margin = new Thickness(24), Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] };

    public PodcastDetailPage()
    {
        Content = Header;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is AbstractLibraryEntity entity) Header.Text = $"Podcast {entity.Pk}";
    }
}
