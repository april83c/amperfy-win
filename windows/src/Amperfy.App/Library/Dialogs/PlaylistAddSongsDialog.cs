using Amperfy.App.Controls;
using Amperfy.App.Services;
using Amperfy.Core.Model;
using Amperfy.Core.Storage;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Amperfy.App.Library.Dialogs;

/// "Add Songs" dialog of the playlist edit mode (desktop replacement of PlaylistAddLibraryVC and
/// its PlaylistAdd* browser screens): search songs, albums or artists of the library and pick
/// several of them; returns the songs to add.
public sealed class PlaylistAddSongsDialog
{
    private enum Scope { Songs, Albums, Artists }

    private const int ResultLimit = 200;

    private readonly AppServices _services = AppServices.Instance;
    private readonly Account _account;
    private readonly TextBox _searchBox = new() { PlaceholderText = "Search in \"Library\"" };
    private readonly ComboBox _scopeBox = new() { MinWidth = 120 };
    private readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.Multiple, Height = 380 };
    private readonly TextBlock _selectionInfo = Ui.Text("", "CaptionTextBlockStyle", secondary: true);
    private readonly Dictionary<string, object> _selected = [];
    private readonly DispatcherQueueTimer _searchTimer;
    private Scope _scope = Scope.Songs;
    private bool _isRefreshing;

    private PlaylistAddSongsDialog(Account account)
    {
        _account = account;
        _searchTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _searchTimer.Interval = TimeSpan.FromMilliseconds(300);
        _searchTimer.IsRepeating = false;
        _searchTimer.Tick += (_, _) => _ = SearchAsync();
    }

    /// Shows the dialog; returns the picked songs (empty when canceled).
    public static async Task<List<Song>> ShowAsync(Account account, string playlistName)
    {
        var dialog = new PlaylistAddSongsDialog(account);
        return await dialog.RunAsync(playlistName);
    }

    private static string Key(object entity) => entity switch
    {
        AbstractLibraryEntity e => $"{e.GetType().Name}-{e.Pk}",
        _ => entity.GetHashCode().ToString(),
    };

    private async Task<List<Song>> RunAsync(string playlistName)
    {
        var root = new StackPanel { Spacing = 12, MinWidth = 460 };
        var searchRow = new Grid { ColumnSpacing = 8 };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.Children.Add(_searchBox);
        _scopeBox.Items.Add(new ComboBoxItem { Content = "Songs", Tag = Scope.Songs });
        _scopeBox.Items.Add(new ComboBoxItem { Content = "Albums", Tag = Scope.Albums });
        _scopeBox.Items.Add(new ComboBoxItem { Content = "Artists", Tag = Scope.Artists });
        _scopeBox.SelectedIndex = 0;
        Grid.SetColumn(_scopeBox, 1);
        searchRow.Children.Add(_scopeBox);
        root.Children.Add(searchRow);
        root.Children.Add(_list);
        root.Children.Add(_selectionInfo);

        _searchBox.TextChanged += (_, _) =>
        {
            _searchTimer.Stop();
            _searchTimer.Start();
        };
        _scopeBox.SelectionChanged += (_, _) =>
        {
            if (_scopeBox.SelectedItem is ComboBoxItem { Tag: Scope scope })
            {
                _scope = scope;
                RefreshResults();
            }
        };
        _list.SelectionChanged += (_, _) =>
        {
            if (_isRefreshing) return;
            foreach (var item in _list.Items.OfType<ListViewItem>())
            {
                if (item.Tag is not { } entity) continue;
                if (item.IsSelected) _selected[Key(entity)] = entity;
                else _selected.Remove(Key(entity));
            }
            UpdateSelectionInfo();
        };

        var dialog = new ContentDialog
        {
            Title = $"Add Songs to \"{playlistName}\"",
            Content = root,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        RefreshResults();
        UpdateSelectionInfo();
        _searchBox.Loaded += (_, _) => _searchBox.Focus(FocusState.Programmatic);
        if (await DialogHelper.ShowAsync(dialog) != ContentDialogResult.Primary || _selected.Count == 0) return [];

        var songs = new List<Song>();
        foreach (var entity in _selected.Values)
        {
            switch (entity)
            {
                case Song song:
                    songs.Add(song);
                    break;
                case IPlayableContainable container:
                    songs.AddRange((await EntityActions.GetPlayablesAsync(container)).OfType<Song>());
                    break;
            }
        }
        return songs;
    }

    private void UpdateSelectionInfo() =>
        _selectionInfo.Text = _selected.Count == 0 ? "Select songs, albums or artists to add." : $"{_selected.Count} selected";

    private async Task SearchAsync()
    {
        RefreshResults();
        var text = _searchBox.Text ?? "";
        if (string.IsNullOrEmpty(text) || !_services.Settings.User.IsOnlineMode) return;
        var syncer = EntityActions.SyncerFor(_account);
        var scope = _scope;
        var ok = await CategoryPageHelper.SyncAsync("Search", () => scope switch
        {
            Scope.Albums => syncer.SearchAlbumsAsync(text),
            Scope.Artists => syncer.SearchArtistsAsync(text),
            _ => syncer.SearchSongsAsync(text),
        }, displayPopup: false);
        if (ok && _searchBox.Text == text && _scope == scope) RefreshResults();
    }

    private void RefreshResults()
    {
        _isRefreshing = true;
        try
        {
            _list.Items.Clear();
            var text = _searchBox.Text ?? "";
            IEnumerable<object> results = _scope switch
            {
                Scope.Albums => LibraryStorage.SortAlbums(_services.Library.QueryAlbums(_account, text, false, DisplayCategoryFilter.All), AlbumElementSortType.Name)
                    .Take(ResultLimit).ToList(),
                Scope.Artists => LibraryStorage.SortArtists(_services.Library.QueryArtists(_account, text, false, ArtistCategoryFilter.All), ArtistElementSortType.Name)
                    .Take(ResultLimit).ToList(),
                _ => LibraryStorage.SortSongs(_services.Library.QuerySongs(_account, text, false, DisplayCategoryFilter.All), SongElementSortType.Name)
                    .Take(ResultLimit).ToList(),
            };
            foreach (var entity in results)
            {
                var item = new ListViewItem { Tag = entity, Content = BuildRow(entity), Padding = new Thickness(8, 4, 8, 4) };
                _list.Items.Add(item);
                if (_selected.ContainsKey(Key(entity))) item.IsSelected = true;
            }
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private static UIElement BuildRow(object entity)
    {
        var grid = new Grid { ColumnSpacing = 12, Height = 44 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new ArtworkImage { Width = 36, Height = 36, DecodeSize = 72, Entity = entity, CornerRadius = new CornerRadius(4) });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Text(LibraryText.Title(entity), "BodyTextBlockStyle"));
        var subtitle = entity switch
        {
            Song song => LibraryText.PlayableSubtitle(song, withAlbum: true),
            IPlayableContainable container => LibraryText.Info(container, DetailType.Short),
            _ => "",
        };
        text.Children.Add(Ui.Text(subtitle, "CaptionTextBlockStyle", secondary: true));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }
}
