using Amperfy.App.Helpers;
using Amperfy.App.Library;
using Amperfy.App.Services;
using Amperfy.Core.Model;
using Amperfy.Core.Storage;
using Amperfy.Core.Sync;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Amperfy.App.Pages;

/// Podcasts (port of PodcastsVC): podcasts sorted by name or all episodes sorted by release date,
/// cached filter, search, refresh (newest episodes).
public sealed partial class PodcastsPage : Page, ILibraryCategoryPage
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly LibraryListContext _context = new();
    private readonly LibraryListController _controller;
    private Account? _account;
    private bool _onlyCached;

    public LibraryDisplayType LibraryType { get; private set; } = LibraryDisplayType.Podcasts;

    public PodcastsPage()
    {
        InitializeComponent();
        _context.HostPageType = typeof(PodcastsPage);
        _controller = new LibraryListController(ItemsList, _context, LibraryListMode.Mixed);
        Header.Title = "Podcasts";
        Header.FilterPlaceholder = "Search in \"Podcasts\"";
        Header.IsPlayVisible = false;
        Header.IsShuffleVisible = false;
        Header.FilterChanged += (_, _) => Reload();
        Header.AttachAccelerators(this, () => _ = RefreshAsync());
    }

    private PodcastsShowType ShowType => _services.Settings.User.PodcastsShowSetting;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is LibraryDisplayType type) LibraryType = type;
        _account = _services.ActiveAccount;
        LibraryEventHub.EnsureInitialized();
        LibraryEventHub.OfflineModeChanged += OnOfflineModeChanged;
        _onlyCached = _services.Settings.User.IsOfflineMode;
        BuildCommands();
        Reload();
        _ = SyncNewestEpisodesAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        LibraryEventHub.OfflineModeChanged -= OnOfflineModeChanged;
    }

    private void OnOfflineModeChanged()
    {
        _onlyCached = _services.Settings.User.IsOfflineMode;
        BuildCommands();
        Reload();
    }

    private void BuildCommands()
    {
        Header.Commands.PrimaryCommands.Clear();
        Header.Commands.SecondaryCommands.Clear();
        Header.AddMenuCommand("Show", LibraryGlyphs.Sort, () =>
        [
            Ui.RadioItem("Podcasts sorted by name", "show", ShowType == PodcastsShowType.Podcasts, () => ChangeShowType(PodcastsShowType.Podcasts)),
            Ui.RadioItem("Episodes sorted by release date", "show", ShowType == PodcastsShowType.EpisodesSortedByReleaseDate,
                () => ChangeShowType(PodcastsShowType.EpisodesSortedByReleaseDate)),
        ]);
        CategoryPageHelper.AddCachedToggle(Header, () => _onlyCached, isChecked =>
        {
            _onlyCached = isChecked;
            Reload();
        });
        Header.AddCommand("Refresh", Icons.Refresh, () => _ = RefreshAsync(), "Refresh (F5)");
    }

    private void ChangeShowType(PodcastsShowType showType)
    {
        _services.Settings.User.PodcastsShowSetting = showType;
        Reload();
        _ = SyncNewestEpisodesAsync();
    }

    private void Reload()
    {
        if (_account is null) return;
        var onlyCached = _onlyCached || _services.Settings.User.IsOfflineMode;
        int count;
        if (ShowType == PodcastsShowType.Podcasts)
        {
            var query = LibraryStorage.SortPodcasts(_services.Library.QueryPodcasts(_account, Header.FilterText, onlyCached));
            count = query.Count();
            _controller.SetIncrementalSource((skip, take) => query.Skip(skip).Take(take).ToList(), count);
            Header.Info = Ui.Plural(count, "Podcast", "Podcasts");
            Header.FilterPlaceholder = "Search in \"Podcasts\"";
            CategoryPageHelper.UpdateEmptyState(EmptyHost, count == 0, !string.IsNullOrEmpty(Header.FilterText), Icons.Podcast, "Podcasts");
        }
        else
        {
            var query = LibraryStorage.SortEpisodesByPublishDate(_services.Library.QueryPodcastEpisodes(_account, Header.FilterText, onlyCached));
            count = query.Count();
            _controller.SetIncrementalSource((skip, take) => query.Skip(skip).Take(take).ToList(), count, pageSize: 50);
            Header.Info = Ui.Plural(count, "Episode", "Episodes");
            Header.FilterPlaceholder = "Search in \"Podcast Episodes\"";
            CategoryPageHelper.UpdateEmptyState(EmptyHost, count == 0, !string.IsNullOrEmpty(Header.FilterText), Icons.Podcast, "Podcast Episodes");
        }
    }

    private async Task SyncNewestEpisodesAsync()
    {
        if (_account is not { } account) return;
        var meta = _services.Kit.GetMeta(account.Info);
        if (await CategoryPageHelper.SyncAsync("Podcasts Sync", () =>
                new AutoDownloadLibrarySyncer(_services.Library, _services.Settings, account, meta.LibrarySyncer, meta.PlayableDownloadManager)
                    .SyncNewestPodcastEpisodesAsync(), displayPopup: false))
            Reload();
    }

    private async Task RefreshAsync()
    {
        if (_account is not { } account || !_services.Settings.User.IsOnlineMode) return;
        Header.IsBusy = true;
        await CategoryPageHelper.SyncAsync("Podcasts Sync", () => EntityActions.SyncerFor(account).SyncDownPodcastsWithoutEpisodesAsync());
        await SyncNewestEpisodesAsync();
        Header.IsBusy = false;
        Reload();
    }
}
