using Amperfy.App.Controls;
using Amperfy.App.Helpers;
using Amperfy.App.Library;
using Amperfy.App.Library.Dialogs;
using Amperfy.App.Services;
using Amperfy.Core.Model;
using Amperfy.Core.Player;
using Amperfy.Core.Storage;
using Amperfy.Core.Sync;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using VirtualKey = Windows.System.VirtualKey;

namespace Amperfy.App.Pages;

/// Home (port of HomeVC + HomeManager + HomeEditorVC): configurable sections (recently played /
/// newest / random albums, random artists / genres / songs, recently played playlists, newest
/// podcast episodes, podcasts, radios) shown as rows of tiles. The sections are chosen and ordered
/// in "Edit" and stored in the account settings (AccountSetting.HomeSections).
public sealed partial class HomePage : Page
{
    /// Swift HomeManager.sectionMaxItemCount
    private const int SectionMaxItemCount = 20;

    private readonly AppServices _services = AppServices.Instance;
    private readonly Dictionary<HomeSection, HomeSectionView> _sectionViews = [];
    private readonly Dictionary<HomeSection, LibraryListContext> _contexts = [];
    private Account? _account;
    private List<HomeSection> _sections = [];

    public HomePage()
    {
        InitializeComponent();
        HeaderButtons.Children.Add(Ui.IconButton(Icons.Refresh, "Refresh (F5)", (_, _) => _ = RefreshAsync()));
        HeaderButtons.Children.Add(Ui.TextButton("Edit", Icons.Edit, (_, _) => _ = EditSectionsAsync(), tooltip: "Choose the home sections"));
        var refresh = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = VirtualKey.F5 };
        refresh.Invoked += (sender, e) =>
        {
            e.Handled = true;
            _ = RefreshAsync();
        };
        KeyboardAccelerators.Add(refresh);
    }

    private bool IsOfflineMode => _services.Settings.User.IsOfflineMode;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _account = _services.ActiveAccount;
        LibraryEventHub.EnsureInitialized();
        LibraryEventHub.OfflineModeChanged += OnOfflineModeChanged;
        BuildSections();
        _ = UpdateFromRemoteAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        LibraryEventHub.OfflineModeChanged -= OnOfflineModeChanged;
    }

    private void OnOfflineModeChanged() => BuildSections();

    private void BuildSections()
    {
        SectionsHost.Children.Clear();
        _sectionViews.Clear();
        _contexts.Clear();
        if (_account is null) return;
        _sections = _services.Settings.Accounts.GetSetting(_account.Info).HomeSections.Distinct().ToList();
        foreach (var section in _sections)
        {
            var context = new LibraryListContext { HostPageType = typeof(HomePage) };
            var view = new HomeSectionView
            {
                Title = section.Title(),
                IsRefreshable = section.IsRandomSection(),
                SeeAll = SeeAllAction(section),
            };
            view.RefreshRequested += () => UpdateSection(section);
            _contexts[section] = context;
            _sectionViews[section] = view;
            SectionsHost.Children.Add(view);
            UpdateSection(section);
        }
        if (_sections.Count == 0)
        {
            EmptyHost.Child ??= Ui.EmptyState(Icons.Home, "No Sections", "Use \"Edit\" to choose the sections of the home page.");
            EmptyHost.Visibility = Visibility.Visible;
        }
        else
        {
            EmptyHost.Visibility = Visibility.Collapsed;
        }
    }

    /// Loads the elements of a section (Swift: HomeManager update functions).
    private void UpdateSection(HomeSection section)
    {
        if (_account is not { } account || !_sectionViews.TryGetValue(section, out var view)) return;
        var context = _contexts[section];
        var offline = IsOfflineMode;
        var library = _services.Library;
        List<object> entities;
        try
        {
            entities = section switch
            {
                HomeSection.RecentlyPlayedAlbums => LibraryStorage.SortAlbums(library.QueryAlbums(account, "", offline, DisplayCategoryFilter.Recent), AlbumElementSortType.Recent)
                    .Take(SectionMaxItemCount).ToList<object>(),
                HomeSection.NewestAlbums => LibraryStorage.SortAlbums(library.QueryAlbums(account, "", offline, DisplayCategoryFilter.Newest), AlbumElementSortType.Newest)
                    .Take(SectionMaxItemCount).ToList<object>(),
                HomeSection.RandomAlbums => library.GetRandomAlbums(account, SectionMaxItemCount, offline).ToList<object>(),
                HomeSection.RandomArtists => library.GetRandomArtists(account, SectionMaxItemCount, offline).ToList<object>(),
                HomeSection.RandomGenres => library.GetRandomGenres(account, SectionMaxItemCount).ToList<object>(),
                HomeSection.RandomSongs => library.GetRandomSongs(account, SectionMaxItemCount, offline).ToList<object>(),
                HomeSection.LastTimePlayedPlaylists => LibraryStorage.SortPlaylists(
                        library.QueryPlaylists(account, "", offline ? PlaylistSearchCategory.Cached : PlaylistSearchCategory.All), PlaylistSortType.LastPlayed)
                    .Take(SectionMaxItemCount).ToList<object>(),
                HomeSection.NewestPodcastEpisodes => LibraryStorage.SortEpisodesByPublishDate(library.QueryPodcastEpisodes(account, "", offline))
                    .Take(SectionMaxItemCount).ToList<object>(),
                HomeSection.Podcasts => LibraryStorage.SortPodcasts(library.QueryPodcasts(account, "", offline))
                    .Take(SectionMaxItemCount).ToList<object>(),
                HomeSection.Radios => library.QuerySortedRadios(account).Take(SectionMaxItemCount).ToList<object>(),
                _ => [],
            };
        }
        catch (Exception ex)
        {
            _services.EventLogger.Report("Home", ex, displayPopup: false);
            entities = [];
        }
        var playables = entities.OfType<AbstractPlayable>().Where(EntityActions.IsPlayable).ToList();
        context.PlayContextProvider = item => item.Playable is { } playable
            ? new PlayContext(section.Title(), Math.Max(0, playables.IndexOf(playable)), playables)
            : null;
        var index = 0;
        view.SetItems(entities.Select(e => new LibraryItem(e, context, index++)).ToList());
    }

    private Action? SeeAllAction(HomeSection section)
    {
        LibraryDisplayType? type = section switch
        {
            HomeSection.RecentlyPlayedAlbums => LibraryDisplayType.RecentAlbums,
            HomeSection.NewestAlbums => LibraryDisplayType.NewestAlbums,
            HomeSection.RandomAlbums => LibraryDisplayType.Albums,
            HomeSection.RandomArtists => LibraryDisplayType.Artists,
            HomeSection.RandomGenres => LibraryDisplayType.Genres,
            HomeSection.RandomSongs => LibraryDisplayType.Songs,
            HomeSection.LastTimePlayedPlaylists => LibraryDisplayType.Playlists,
            HomeSection.NewestPodcastEpisodes or HomeSection.Podcasts => LibraryDisplayType.Podcasts,
            HomeSection.Radios => LibraryDisplayType.Radios,
            _ => null,
        };
        if (type is not { } libraryType) return null;
        return () =>
        {
            var (page, parameter) = PageRegistry.ForLibraryType(libraryType);
            _services.Navigation.Navigate(page, parameter);
        };
    }

    /// Swift: HomeManager.updateFromRemote
    private async Task UpdateFromRemoteAsync()
    {
        if (_account is not { } account || !_services.Settings.User.IsOnlineMode) return;
        var meta = _services.Kit.GetMeta(account.Info);
        var tasks = new List<Task>();
        if (_sections.Contains(HomeSection.NewestAlbums))
        {
            tasks.Add(SyncSectionAsync(HomeSection.NewestAlbums, "Newest Albums Sync", () =>
                new AutoDownloadLibrarySyncer(_services.Library, _services.Settings, account, meta.LibrarySyncer, meta.PlayableDownloadManager)
                    .SyncNewestLibraryElementsAsync(0, SectionMaxItemCount)));
        }
        if (_sections.Contains(HomeSection.RecentlyPlayedAlbums))
            tasks.Add(SyncSectionAsync(HomeSection.RecentlyPlayedAlbums, "Recent Albums Sync", () => meta.LibrarySyncer.SyncRecentAlbumsAsync(0, SectionMaxItemCount)));
        if (_sections.Contains(HomeSection.LastTimePlayedPlaylists))
            tasks.Add(SyncSectionAsync(HomeSection.LastTimePlayedPlaylists, "Playlists Sync", () => meta.LibrarySyncer.SyncDownPlaylistsWithoutSongsAsync()));
        if (_sections.Contains(HomeSection.NewestPodcastEpisodes) || _sections.Contains(HomeSection.Podcasts))
        {
            tasks.Add(SyncSectionAsync(HomeSection.NewestPodcastEpisodes, "Podcasts Sync", () =>
                new AutoDownloadLibrarySyncer(_services.Library, _services.Settings, account, meta.LibrarySyncer, meta.PlayableDownloadManager)
                    .SyncNewestPodcastEpisodesAsync(), alsoUpdate: HomeSection.Podcasts));
        }
        if (_sections.Contains(HomeSection.Radios))
            tasks.Add(SyncSectionAsync(HomeSection.Radios, "Radios Sync", () => meta.LibrarySyncer.SyncRadiosAsync()));
        await Task.WhenAll(tasks);
    }

    private async Task SyncSectionAsync(HomeSection section, string topic, Func<Task> sync, HomeSection? alsoUpdate = null)
    {
        if (await CategoryPageHelper.SyncAsync(topic, sync, displayPopup: false))
        {
            UpdateSection(section);
            if (alsoUpdate is { } other) UpdateSection(other);
        }
    }

    private async Task RefreshAsync()
    {
        foreach (var section in _sections) UpdateSection(section);
        await UpdateFromRemoteAsync();
    }

    private async Task EditSectionsAsync()
    {
        if (_account is not { } account) return;
        var newSections = await HomeEditorDialog.ShowAsync(_sections);
        if (newSections is null) return;
        _services.Settings.Accounts.UpdateSetting(account.Info, setting => setting.HomeSections = newSections);
        BuildSections();
        _ = UpdateFromRemoteAsync();
    }
}
