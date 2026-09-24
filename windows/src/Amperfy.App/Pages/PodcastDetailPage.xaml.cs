using Amperfy.App.Controls;
using Amperfy.App.Library;
using Amperfy.App.Services;
using Amperfy.Core.Common;
using Amperfy.Core.Model;
using Amperfy.Core.Player;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Amperfy.App.Pages;

/// Podcast detail (port of PodcastDetailVC): header with description and "Newest Episode" play
/// button; the episodes (status, progress, download, description); search and cached filter.
/// The podcast is synced from the server when shown.
public sealed partial class PodcastDetailPage : Page
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly LibraryListContext _context = new();
    private readonly LibraryListController _controller;
    private Podcast? _podcast;
    private object? _scrollTo;
    private IQueryable<PodcastEpisode>? _query;

    public PodcastDetailPage()
    {
        InitializeComponent();
        _context.HostPageType = typeof(PodcastDetailPage);
        _context.Changed = () => Header.Refresh();
        _controller = new LibraryListController(ItemsList, _context, LibraryListMode.Playables);
        Toolbar.FilterPlaceholder = "Search in \"Podcast\"";
        Toolbar.FilterChanged += Reload;
        Toolbar.RefreshRequested += () => _ = SyncAsync(showBusy: true);
        Toolbar.AttachAccelerators(this);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        (_podcast, _scrollTo) = NavigationArgs.Unpack<Podcast>(e.Parameter);
        LibraryEventHub.EnsureInitialized();
        LibraryEventHub.OfflineModeChanged += OnOfflineModeChanged;
        if (_podcast is null) return;
        ConfigureHeader();
        Reload();
        _ = SyncAsync(showBusy: false);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        LibraryEventHub.OfflineModeChanged -= OnOfflineModeChanged;
    }

    private void OnOfflineModeChanged()
    {
        Toolbar.UpdateOfflineMode();
        Header.Refresh();
        Reload();
    }

    private void ConfigureHeader()
    {
        if (_podcast is not { } podcast) return;
        Header.Configure(podcast, new DetailHeaderOptions
        {
            HostPageType = typeof(PodcastDetailPage),
            PlayText = "Newest Episode",
            IsShuffleHidden = true,
            Description = podcast.Depiction.Html2String(),
            PlayContext = () =>
            {
                var newest = _services.Library.QueryPodcastEpisodes(podcast, "", Toolbar.OnlyCached).ToList()
                    .FirstOrDefault(EntityActions.IsPlayable);
                return Task.FromResult<PlayContext?>(newest is null ? null : new PlayContext(podcast, 0, [newest]));
            },
            Changed = Reload,
        });
    }

    private void Reload()
    {
        if (_podcast is not { } podcast) return;
        var filter = Toolbar.FilterText;
        var query = _services.Library.QueryPodcastEpisodes(podcast, filter, Toolbar.OnlyCached);
        _query = query;
        var count = query.Count();
        _controller.SetIncrementalSource((skip, take) => query.Skip(skip).Take(take).ToList(), count, pageSize: 50);
        EmptyText.Text = string.IsNullOrEmpty(filter) && !Toolbar.OnlyCached ? "No episodes" : "No results";
        EmptyText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ScrollToTarget();
    }

    private void ScrollToTarget()
    {
        if (_scrollTo is not PodcastEpisode episode || _query is null) return;
        var index = _query.ToList().IndexOf(episode);
        if (index < 0) return;
        _scrollTo = null;
        _controller.ScrollToIndex(index);
        if (_controller.FindItem(e => ReferenceEquals(e, episode)) is { } item) ItemsList.SelectedItem = item;
    }

    private async Task SyncAsync(bool showBusy)
    {
        if (_podcast is not { Account: { } account } podcast || !_services.Settings.User.IsOnlineMode) return;
        Header.IsBusy = showBusy;
        await CategoryPageHelper.SyncAsync("Podcast Sync", () => EntityActions.SyncerFor(account).SyncAsync(podcast), displayPopup: showBusy);
        Header.IsBusy = false;
        if (_podcast != podcast) return;
        ConfigureHeader();
        Reload();
    }
}
