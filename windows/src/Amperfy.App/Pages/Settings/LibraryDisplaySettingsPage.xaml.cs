using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Amperfy.App.Helpers;
using Amperfy.App.Services;
using Amperfy.Core.Common;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Amperfy.App.Pages.Settings;

/// Row of a reorderable, checkable settings list (library categories, home sections).
public sealed class ReorderableSettingItem : INotifyPropertyChanged
{
    private readonly Action _changed;
    private bool _isEnabled;

    public ReorderableSettingItem(object value, string title, string glyph, bool isEnabled, Action changed)
    {
        Value = value;
        Title = title;
        Glyph = glyph;
        _isEnabled = isEnabled;
        _changed = changed;
    }

    public object Value { get; }
    public string Title { get; }
    public string Glyph { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
            _changed();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// Library display settings of the active account (port of the iOS library/home "edit" mode):
/// which library categories the sidebar shows and in which order, and the home page sections.
/// Changes are saved immediately; the shell rebuilds the sidebar on LibraryDisplaySettingsChanged.
public sealed partial class LibraryDisplaySettingsPage : Page
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly ObservableCollection<ReorderableSettingItem> _categories = [];
    private readonly ObservableCollection<ReorderableSettingItem> _homeSections = [];
    private readonly List<IDisposable> _subscriptions = [];
    private bool _isLoading;
    private bool _isCategoriesSavePending;
    private bool _isHomeSavePending;

    public LibraryDisplaySettingsPage()
    {
        InitializeComponent();
        CategoriesList.ItemsSource = _categories;
        HomeSectionsList.ItemsSource = _homeSections;
        _categories.CollectionChanged += (_, e) => { if (IsOrderChange(e)) ScheduleCategoriesSave(); };
        _homeSections.CollectionChanged += (_, e) => { if (IsOrderChange(e)) ScheduleHomeSave(); };
        ResetCategoriesButton.Click += (_, _) => ResetCategories();
        ResetHomeButton.Click += (_, _) => ResetHomeSections();
        Loaded += (_, _) =>
        {
            if (_subscriptions.Count == 0)
                _subscriptions.Add(_services.Notifications.Register(AmperfyNotification.AccountActiveChanged, _ => Load()));
            Load();
        };
        Unloaded += (_, _) =>
        {
            foreach (var s in _subscriptions) s.Dispose();
            _subscriptions.Clear();
        };
    }

    private bool IsOrderChange(NotifyCollectionChangedEventArgs e) => !_isLoading && e.Action != NotifyCollectionChangedAction.Reset;

    private void Load()
    {
        _isLoading = true;
        try
        {
            _categories.Clear();
            _homeSections.Clear();
            var info = _services.Settings.Accounts.Active;
            var isAvailable = info is not null;
            CategoriesList.IsEnabled = HomeSectionsList.IsEnabled = ResetCategoriesButton.IsEnabled = ResetHomeButton.IsEnabled = isAvailable;
            if (info is null)
            {
                AccountHintText.Text = "You aren't logged in yet.";
                return;
            }
            var setting = _services.Settings.Accounts.GetSetting(info);
            AccountHintText.Text = $"These settings belong to the account {setting.LoginCredentials?.Username} · {setting.LoginCredentials?.DisplayServerUrl}.";
            FillCategories(setting.LibraryDisplaySettings);
            FillHomeSections(setting.HomeSections);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void FillCategories(LibraryDisplaySettings displaySettings)
    {
        foreach (var type in displaySettings.InUse.Distinct())
            _categories.Add(new ReorderableSettingItem(type, type.DisplayName(), Icons.For(type), true, ScheduleCategoriesSave));
        foreach (var type in displaySettings.NotUsed)
            _categories.Add(new ReorderableSettingItem(type, type.DisplayName(), Icons.For(type), false, ScheduleCategoriesSave));
    }

    private void FillHomeSections(IReadOnlyList<HomeSection> sections)
    {
        foreach (var section in sections.Distinct())
            _homeSections.Add(new ReorderableSettingItem(section, section.Title(), Glyph(section), true, ScheduleHomeSave));
        foreach (var section in Enum.GetValues<HomeSection>().Where(s => !sections.Contains(s)))
            _homeSections.Add(new ReorderableSettingItem(section, section.Title(), Glyph(section), false, ScheduleHomeSave));
    }

    private static string Glyph(HomeSection section) => section switch
    {
        HomeSection.LastTimePlayedPlaylists => Icons.Playlist,
        HomeSection.RecentlyPlayedAlbums => Icons.Recent,
        HomeSection.NewestAlbums => Icons.Newest,
        HomeSection.RandomAlbums => Icons.Album,
        HomeSection.NewestPodcastEpisodes or HomeSection.Podcasts => Icons.Podcast,
        HomeSection.Radios => Icons.Radio,
        HomeSection.RandomArtists => Icons.Artist,
        HomeSection.RandomGenres => Icons.Genre,
        _ => Icons.Song,
    };

    // A drag reorder is a Remove followed by an Add: save once after both (coalesced on the dispatcher).
    private void ScheduleCategoriesSave()
    {
        if (_isLoading || _isCategoriesSavePending) return;
        _isCategoriesSavePending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _isCategoriesSavePending = false;
            SaveCategories();
        });
    }

    private void ScheduleHomeSave()
    {
        if (_isLoading || _isHomeSavePending) return;
        _isHomeSavePending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _isHomeSavePending = false;
            SaveHomeSections();
        });
    }

    private void SaveCategories()
    {
        if (_services.Settings.Accounts.Active is not { } info) return;
        var inUse = _categories.Where(i => i.IsEnabled).Select(i => (LibraryDisplayType)i.Value).ToList();
        _services.Settings.Accounts.UpdateSetting(info, s => s.LibraryDisplaySettings = new LibraryDisplaySettings(inUse));
        _services.Notifications.Post(AmperfyNotification.LibraryDisplaySettingsChanged, this);
    }

    private void SaveHomeSections()
    {
        if (_services.Settings.Accounts.Active is not { } info) return;
        var sections = _homeSections.Where(i => i.IsEnabled).Select(i => (HomeSection)i.Value).ToList();
        _services.Settings.Accounts.UpdateSetting(info, s => s.HomeSections = sections);
    }

    private void ResetCategories()
    {
        _isLoading = true;
        _categories.Clear();
        FillCategories(LibraryDisplaySettings.DefaultSettings);
        _isLoading = false;
        SaveCategories();
    }

    private void ResetHomeSections()
    {
        _isLoading = true;
        _homeSections.Clear();
        FillHomeSections(SettingEnumerationExtensions.DefaultHomeSections);
        _isLoading = false;
        SaveHomeSections();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(sender, -1);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(sender, 1);

    private void Move(object sender, int delta)
    {
        if ((sender as FrameworkElement)?.DataContext is not ReorderableSettingItem item) return;
        var list = _categories.Contains(item) ? _categories : _homeSections.Contains(item) ? _homeSections : null;
        if (list is null) return;
        var index = list.IndexOf(item);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= list.Count) return;
        list.Move(index, target);
    }
}
