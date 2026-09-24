using Amperfy.App.Helpers;
using Amperfy.App.Library;
using Amperfy.App.Services;
using Amperfy.Core.Common;
using Amperfy.Core.Model;
using Amperfy.Core.Storage;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;

namespace Amperfy.App.Pages;

/// Search (port of SearchVC): the text of the title bar search box is searched in the local
/// library (artists, albums, playlists, songs, podcasts, episodes, genres, radios; grouped results
/// with the best matches first) and on the server (artists, albums, songs) in online mode. Without
/// text the search history is shown. A category filter shows all results of one category.
public sealed partial class SearchPage : Page
{
    private enum Category { All, Artists, Albums, Songs, Playlists, Podcasts, Episodes, Genres, Radios }

    /// Swift SearchVC.categoryItemLimit (results per category in the grouped view).
    private const int CategoryItemLimit = 10;
    /// Number of local matches ranked by the fuzzy search per category.
    private const int CandidateLimit = 200;

    private readonly AppServices _services = AppServices.Instance;
    private readonly LibraryListContext _context = new() { IsEpisodeStyle = false, ShowAlbumInSubtitle = true };
    private readonly LibraryListController _controller;
    private readonly SelectorBar _categoryBar = new();
    private readonly Dictionary<SelectorBarItem, Category> _categoryItems = [];
    private readonly ToggleButton _cachedToggle;
    private readonly Button _clearHistoryButton;
    private readonly DispatcherQueueTimer _localTimer;
    private readonly DispatcherQueueTimer _serverTimer;
    private Account? _account;
    private string _text = "";
    private Category _category = Category.All;
    private int _serverSearchGeneration;

    public SearchPage()
    {
        InitializeComponent();
        _context.HostPageType = typeof(SearchPage);
        _controller = new LibraryListController(ItemsList, _context, LibraryListMode.Mixed);
        _controller.ItemActivated = OnItemActivated;

        foreach (var (category, text, glyph) in new[]
                 {
                     (Category.All, "All", Icons.Search), (Category.Artists, "Artists", Icons.Artist), (Category.Albums, "Albums", Icons.Album),
                     (Category.Songs, "Songs", Icons.Song), (Category.Playlists, "Playlists", Icons.Playlist),
                     (Category.Podcasts, "Podcasts", Icons.Podcast), (Category.Episodes, "Episodes", Icons.Podcast),
                     (Category.Genres, "Genres", Icons.Genre), (Category.Radios, "Radios", Icons.Radio),
                 })
        {
            var item = new SelectorBarItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
            _categoryItems[item] = category;
            _categoryBar.Items.Add(item);
        }
        _categoryBar.SelectedItem = _categoryBar.Items[0];
        _categoryBar.SelectionChanged += (_, _) =>
        {
            if (_categoryBar.SelectedItem is { } selected && _categoryItems.TryGetValue(selected, out var category) && category != _category)
            {
                _category = category;
                RunLocalSearch();
            }
        };
        CategoryHost.Children.Add(_categoryBar);

        var cachedContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        cachedContent.Children.Add(Ui.Icon(LibraryGlyphs.Cached, 14));
        cachedContent.Children.Add(new TextBlock { Text = "Cached", VerticalAlignment = VerticalAlignment.Center });
        _cachedToggle = new ToggleButton { Content = cachedContent };
        ToolTipService.SetToolTip(_cachedToggle, "Search only cached items");
        _cachedToggle.Click += (_, _) =>
        {
            RunLocalSearch();
            ScheduleServerSearch(immediately: false);
        };
        HeaderButtons.Children.Add(_cachedToggle);
        _clearHistoryButton = Ui.TextButton("Clear Search History", Icons.Clear, (_, _) => ClearHistory(), tooltip: "Clear Search History");
        HeaderButtons.Children.Add(_clearHistoryButton);

        var dispatcher = DispatcherQueue.GetForCurrentThread();
        _localTimer = dispatcher.CreateTimer();
        _localTimer.Interval = TimeSpan.FromMilliseconds(200);
        _localTimer.IsRepeating = false;
        _localTimer.Tick += (_, _) => RunLocalSearch();
        _serverTimer = dispatcher.CreateTimer();
        _serverTimer.Interval = TimeSpan.FromMilliseconds(700);
        _serverTimer.IsRepeating = false;
        _serverTimer.Tick += (_, _) => _ = RunServerSearchAsync();
    }

    private bool OnlyCached => _cachedToggle.IsChecked == true || _services.Settings.User.IsOfflineMode;

    /// Called by the title bar search box (text changes and submissions).
    public void UpdateSearch(string text, bool isSubmitted)
    {
        _text = text?.Trim() ?? "";
        _localTimer.Stop();
        if (isSubmitted)
        {
            RunLocalSearch();
            ScheduleServerSearch(immediately: true);
        }
        else
        {
            _localTimer.Start();
            ScheduleServerSearch(immediately: false);
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _account = _services.ActiveAccount;
        LibraryEventHub.EnsureInitialized();
        LibraryEventHub.OfflineModeChanged += OnOfflineModeChanged;
        UpdateOfflineMode();
        _text = (e.Parameter as string)?.Trim() ?? "";
        RunLocalSearch();
        if (_text.Length > 0) ScheduleServerSearch(immediately: true);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        LibraryEventHub.OfflineModeChanged -= OnOfflineModeChanged;
        _localTimer.Stop();
        _serverTimer.Stop();
    }

    private void OnOfflineModeChanged()
    {
        UpdateOfflineMode();
        RunLocalSearch();
    }

    private void UpdateOfflineMode()
    {
        var offline = _services.Settings.User.IsOfflineMode;
        if (offline) _cachedToggle.IsChecked = true;
        _cachedToggle.IsEnabled = !offline;
    }

    // --- local search ----------------------------------------------------------------------------

    private void RunLocalSearch()
    {
        if (_account is not { } account) return;
        try
        {
            if (_text.Length == 0) ShowHistory(account);
            else if (_category == Category.All) ShowGroupedResults(account);
            else ShowCategory(account, _category);
        }
        catch (Exception ex)
        {
            AmperfyLog.Error("SearchPage", $"Search failed: {ex}");
        }
    }

    private void ShowHistory(Account account)
    {
        var history = _services.Library.GetSearchHistory(account)
            .Where(h => h.SearchedPlayableContainable is not null)
            .ToList();
        var items = new List<object>();
        if (history.Count > 0) items.Add(new SectionHeaderItem("Recently Searched"));
        items.AddRange(history);
        _controller.SetItems(items);
        _clearHistoryButton.Visibility = history.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (history.Count == 0)
        {
            EmptyHost.Child = Ui.EmptyState(LibraryGlyphs.History, "No Search History",
                "Your search history will appear here. Type in the search box above to search your library.");
            EmptyHost.Visibility = Visibility.Visible;
        }
        else
        {
            EmptyHost.Visibility = Visibility.Collapsed;
        }
    }

    private IQueryable<Artist> ArtistQuery(Account account) =>
        LibraryStorage.SortArtists(_services.Library.QueryArtists(account, _text, OnlyCached, ArtistCategoryFilter.All), ArtistElementSortType.Name);

    private IQueryable<Album> AlbumQuery(Account account) =>
        LibraryStorage.SortAlbums(_services.Library.QueryAlbums(account, _text, OnlyCached, DisplayCategoryFilter.All), AlbumElementSortType.Name);

    private IQueryable<Song> SongQuery(Account account) =>
        LibraryStorage.SortSongs(_services.Library.QuerySongs(account, _text, OnlyCached, DisplayCategoryFilter.All), SongElementSortType.Name);

    private IQueryable<Playlist> PlaylistQuery(Account account) =>
        LibraryStorage.SortPlaylists(_services.Library.QueryPlaylists(account, _text, OnlyCached ? PlaylistSearchCategory.Cached : PlaylistSearchCategory.All), PlaylistSortType.Name);

    private IQueryable<Podcast> PodcastQuery(Account account) =>
        LibraryStorage.SortPodcasts(_services.Library.QueryPodcasts(account, _text, OnlyCached));

    private IQueryable<PodcastEpisode> EpisodeQuery(Account account) =>
        LibraryStorage.SortEpisodesByPublishDate(_services.Library.QueryPodcastEpisodes(account, _text, OnlyCached));

    private IQueryable<Genre> GenreQuery(Account account) =>
        LibraryStorage.SortGenres(_services.Library.QueryGenres(account, _text, OnlyCached));

    private IQueryable<Radio> RadioQuery(Account account) => _services.Library.QuerySortedRadios(account, _text);

    /// Best matching elements of a query (Swift: FuzzySearcher.findBestMatch + categoryItemLimit).
    private List<T> BestMatches<T>(IQueryable<T> query, Func<T, string> name) =>
        FuzzySearcher.FindBestMatch(query.Take(CandidateLimit).ToList(), name, _text).Take(CategoryItemLimit).ToList();

    private void ShowGroupedResults(Account account)
    {
        var items = new List<object>();
        void AddSection<T>(string title, Category category, IQueryable<T> query, Func<T, string> name) where T : class
        {
            var matches = BestMatches(query, name);
            if (matches.Count == 0) return;
            var total = query.Count();
            items.Add(total > matches.Count
                ? new SectionHeaderItem(title, $"Show all ({total})", () => SelectCategory(category))
                : new SectionHeaderItem(title));
            items.AddRange(matches);
        }

        AddSection("Artists", Category.Artists, ArtistQuery(account), a => a.Name);
        AddSection("Albums", Category.Albums, AlbumQuery(account), a => a.Name);
        AddSection("Playlists", Category.Playlists, PlaylistQuery(account), p => p.Name);
        AddSection("Songs", Category.Songs, SongQuery(account), s => s.Title);
        AddSection("Podcasts", Category.Podcasts, PodcastQuery(account), p => p.Title);
        AddSection("Podcast Episodes", Category.Episodes, EpisodeQuery(account), e => e.Title);
        AddSection("Genres", Category.Genres, GenreQuery(account), g => g.Name);
        AddSection("Radios", Category.Radios, RadioQuery(account), r => r.Title);

        _controller.SetItems(items);
        _clearHistoryButton.Visibility = Visibility.Collapsed;
        UpdateEmptyState(items.Count == 0);
    }

    private void ShowCategory(Account account, Category category)
    {
        int count;
        switch (category)
        {
            case Category.Artists:
            {
                var q = ArtistQuery(account);
                count = q.Count();
                _controller.SetIncrementalSource((skip, take) => q.Skip(skip).Take(take).ToList(), count);
                break;
            }
            case Category.Albums:
            {
                var q = AlbumQuery(account);
                count = q.Count();
                _controller.SetIncrementalSource((skip, take) => q.Skip(skip).Take(take).ToList(), count);
                break;
            }
            case Category.Songs:
            {
                var q = SongQuery(account);
                count = q.Count();
                _controller.SetIncrementalSource((skip, take) => q.Skip(skip).Take(take).ToList(), count);
                break;
            }
            case Category.Playlists:
            {
                var q = PlaylistQuery(account);
                count = q.Count();
                _controller.SetIncrementalSource((skip, take) => q.Skip(skip).Take(take).ToList(), count);
                break;
            }
            case Category.Podcasts:
            {
                var q = PodcastQuery(account);
                count = q.Count();
                _controller.SetIncrementalSource((skip, take) => q.Skip(skip).Take(take).ToList(), count);
                break;
            }
            case Category.Episodes:
            {
                var q = EpisodeQuery(account);
                count = q.Count();
                _controller.SetIncrementalSource((skip, take) => q.Skip(skip).Take(take).ToList(), count);
                break;
            }
            case Category.Genres:
            {
                var q = GenreQuery(account);
                count = q.Count();
                _controller.SetIncrementalSource((skip, take) => q.Skip(skip).Take(take).ToList(), count);
                break;
            }
            default:
            {
                var q = RadioQuery(account);
                count = q.Count();
                _controller.SetIncrementalSource((skip, take) => q.Skip(skip).Take(take).ToList(), count);
                break;
            }
        }
        _clearHistoryButton.Visibility = Visibility.Collapsed;
        UpdateEmptyState(count == 0);
    }

    private void UpdateEmptyState(bool isEmpty)
    {
        if (isEmpty)
        {
            EmptyHost.Child = Ui.EmptyState(Icons.Search, "No Results", $"No results for \"{_text}\". Check the spelling or try a new search.");
            EmptyHost.Visibility = Visibility.Visible;
        }
        else
        {
            EmptyHost.Visibility = Visibility.Collapsed;
        }
    }

    private void SelectCategory(Category category)
    {
        var item = _categoryItems.FirstOrDefault(p => p.Value == category).Key;
        if (item is not null) _categoryBar.SelectedItem = item;
    }

    // --- server search ---------------------------------------------------------------------------

    private void ScheduleServerSearch(bool immediately)
    {
        _serverTimer.Stop();
        if (_text.Length == 0 || OnlyCached || !_services.Settings.User.IsOnlineMode) return;
        if (immediately) _ = RunServerSearchAsync();
        else _serverTimer.Start();
    }

    /// Swift: searchArtists / searchAlbums / searchSongs of the library syncer, then the local
    /// results are updated.
    private async Task RunServerSearchAsync()
    {
        if (_account is not { } account || _text.Length == 0 || OnlyCached || !_services.Settings.User.IsOnlineMode) return;
        var text = _text;
        var generation = ++_serverSearchGeneration;
        var syncer = EntityActions.SyncerFor(account);
        BusyRing.IsActive = true;
        BusyRing.Visibility = Visibility.Visible;
        await Task.WhenAll(
            CategoryPageHelper.SyncAsync("Artists Search", () => syncer.SearchArtistsAsync(text), displayPopup: false),
            CategoryPageHelper.SyncAsync("Albums Search", () => syncer.SearchAlbumsAsync(text), displayPopup: false),
            CategoryPageHelper.SyncAsync("Songs Search", () => syncer.SearchSongsAsync(text), displayPopup: false));
        if (generation != _serverSearchGeneration) return;
        BusyRing.IsActive = false;
        BusyRing.Visibility = Visibility.Collapsed;
        if (text == _text) RunLocalSearch();
    }

    // --- history ---------------------------------------------------------------------------------

    private void OnItemActivated(LibraryItem item)
    {
        if (item.Container is { } container && item.Entity is not SearchHistoryItem) EntityActions.RecordSearchHistory(container);
        LibraryListController.ActivateDefault(item);
        if (_text.Length == 0) ShowHistoryDeferred();
    }

    private void ShowHistoryDeferred() => DispatcherQueue.TryEnqueue(() =>
    {
        if (_text.Length == 0 && _account is { } account && ReferenceEquals(Frame?.Content, this)) ShowHistory(account);
    });

    private void ClearHistory()
    {
        try
        {
            _services.Library.DeleteSearchHistory();
            _services.Library.SaveContext();
        }
        catch (Exception ex)
        {
            _services.EventLogger.Report("Search History", ex);
        }
        RunLocalSearch();
    }
}
