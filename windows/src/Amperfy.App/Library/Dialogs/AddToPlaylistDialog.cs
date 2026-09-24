using Amperfy.App.Controls;
using Amperfy.App.Helpers;
using Amperfy.App.Services;
using Amperfy.Core.Model;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Amperfy.App.Library.Dialogs;

/// "Add to Playlist" dialog (port of PlaylistSelectorVC + NewPlaylistTableHeader): pick one or
/// more playlists of the account (search, sort), or create a new playlist. Duplicates are
/// detected and can be added or skipped. Changes are uploaded to the server.
public sealed class AddToPlaylistDialog
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly Account _account;
    private readonly IReadOnlyList<Song> _songs;
    private readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.Multiple, Height = 360 };
    private readonly TextBox _searchBox = new() { PlaceholderText = "Search in \"Playlists\"" };
    private readonly TextBox _newNameBox = new() { PlaceholderText = "New playlist name" };
    private readonly ComboBox _sortBox = new() { MinWidth = 150 };
    private readonly HashSet<int> _selectedPks = [];
    private PlaylistSortType _sortType;
    private bool _isRefreshing;

    private AddToPlaylistDialog(Account account, IReadOnlyList<Song> songs)
    {
        _account = account;
        _songs = songs;
        _sortType = _services.Settings.User.PlaylistsSortSetting;
    }

    public static async Task ShowAsync(Account account, IReadOnlyList<Song> songs)
    {
        if (songs.Count == 0) return;
        var dialog = new AddToPlaylistDialog(account, songs);
        await dialog.RunAsync();
    }

    private async Task RunAsync()
    {
        var contentDialog = new ContentDialog
        {
            Title = _songs.Count > 1 ? $"Add {_songs.Count} Songs to Playlist" : "Add to Playlist",
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            Content = BuildContent(),
        };
        _list.SelectionChanged += (_, _) =>
        {
            if (_isRefreshing) return;
            foreach (var item in _list.Items.OfType<ListViewItem>())
            {
                if (item.Tag is not Playlist p) continue;
                if (item.IsSelected) _selectedPks.Add(p.Pk);
                else _selectedPks.Remove(p.Pk);
            }
            contentDialog.IsPrimaryButtonEnabled = _selectedPks.Count > 0;
        };
        RefreshList();
        _ = SyncPlaylistsAsync();

        if (await DialogHelper.ShowAsync(contentDialog) != ContentDialogResult.Primary) return;
        var selected = _services.Library.GetPlaylists(_account).Where(p => _selectedPks.Contains(p.Pk)).ToList();
        if (selected.Count == 0) return;
        await AddSongsAsync(selected);
    }

    private UIElement BuildContent()
    {
        var root = new StackPanel { Spacing = 12, MinWidth = 420 };

        var newRow = new Grid { ColumnSpacing = 8 };
        newRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        newRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        newRow.Children.Add(_newNameBox);
        var createButton = Ui.TextButton("Create", Icons.Add, (_, _) => _ = CreatePlaylistAsync(), tooltip: "Create a new playlist");
        Grid.SetColumn(createButton, 1);
        newRow.Children.Add(createButton);
        _newNameBox.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            _ = CreatePlaylistAsync();
        };
        root.Children.Add(newRow);

        var searchRow = new Grid { ColumnSpacing = 8 };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.Children.Add(_searchBox);
        foreach (var (type, text) in new[]
                 {
                     (PlaylistSortType.Name, "Name"), (PlaylistSortType.LastPlayed, "Last time played"),
                     (PlaylistSortType.LastChanged, "Change date"), (PlaylistSortType.Duration, "Duration"),
                 })
        {
            _sortBox.Items.Add(new ComboBoxItem { Content = text, Tag = type });
            if (type == _sortType) _sortBox.SelectedIndex = _sortBox.Items.Count - 1;
        }
        ToolTipService.SetToolTip(_sortBox, "Sort");
        _sortBox.SelectionChanged += (_, _) =>
        {
            if (_sortBox.SelectedItem is ComboBoxItem { Tag: PlaylistSortType type })
            {
                // sort type is not saved permanently (Swift behaviour differs from PlaylistsVC)
                _sortType = type;
                RefreshList();
            }
        };
        Grid.SetColumn(_sortBox, 1);
        searchRow.Children.Add(_sortBox);
        _searchBox.TextChanged += (_, _) => RefreshList();
        root.Children.Add(searchRow);

        root.Children.Add(_list);
        root.Children.Add(Ui.Text("Select one or more playlists.", "CaptionTextBlockStyle", secondary: true));
        return root;
    }

    private void RefreshList()
    {
        _isRefreshing = true;
        try
        {
            _list.Items.Clear();
            var query = _services.Library.QueryPlaylists(_account, _searchBox.Text ?? "", PlaylistSearchCategory.UserOnly);
            var playlists = LibraryStorage.SortPlaylists(query, _sortType).ToList();
            foreach (var playlist in playlists)
            {
                var item = new ListViewItem { Tag = playlist, Content = BuildRow(playlist), Padding = new Thickness(8, 4, 8, 4) };
                _list.Items.Add(item);
                if (_selectedPks.Contains(playlist.Pk)) item.IsSelected = true;
            }
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private static UIElement BuildRow(Playlist playlist)
    {
        var grid = new Grid { ColumnSpacing = 12, Height = 48 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var artwork = new ArtworkImage { Width = 40, Height = 40, DecodeSize = 80, Entity = playlist, CornerRadius = new CornerRadius(4) };
        grid.Children.Add(artwork);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Text(playlist.Name, "BodyTextBlockStyle"));
        var info = LibraryText.Info(playlist, DetailType.Short);
        text.Children.Add(Ui.Text(info, "CaptionTextBlockStyle", secondary: true));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    private async Task SyncPlaylistsAsync()
    {
        if (!_services.Settings.User.IsOnlineMode) return;
        try
        {
            await EntityActions.SyncerFor(_account).SyncDownPlaylistsWithoutSongsAsync();
            RefreshList();
        }
        catch (Exception ex)
        {
            _services.EventLogger.Report("Playlists Sync", ex, displayPopup: false);
        }
    }

    private async Task CreatePlaylistAsync()
    {
        var name = _newNameBox.Text?.Trim() ?? "";
        if (name.Length == 0) return;
        _newNameBox.Text = "";
        var playlist = await EntityActions.CreatePlaylistAsync(_account, name);
        if (playlist is null) return;
        _selectedPks.Add(playlist.Pk);
        RefreshList();
    }

    private async Task AddSongsAsync(List<Playlist> playlists)
    {
        var hasDuplicates = playlists.Any(p => p.NotContains(_songs).Count != _songs.Distinct().Count());
        var skipDuplicates = false;
        if (hasDuplicates)
        {
            var question = new ContentDialog
            {
                Title = "Duplicates",
                Content = new TextBlock
                {
                    Text = playlists.Count == 1 ? "Some Songs are already in this Playlist." : "Some Songs are already in the selected Playlists.",
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = "Add Duplicates",
                SecondaryButtonText = "Skip Duplicates",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Secondary,
            };
            var result = await DialogHelper.ShowAsync(question);
            if (result == ContentDialogResult.None) return;
            skipDuplicates = result == ContentDialogResult.Secondary;
        }

        foreach (var playlist in playlists)
        {
            var songsToAdd = skipDuplicates ? playlist.NotContains(_songs).OfType<Song>().ToList() : _songs.ToList();
            if (songsToAdd.Count == 0) continue;
            try
            {
                if (_services.Settings.User.IsOnlineMode)
                    await EntityActions.SyncerFor(_account).SyncUploadPlaylistAddSongsAsync(playlist, songsToAdd);
                playlist.Append(songsToAdd);
                _services.Library.SaveContext();
                LibraryEventHub.RaiseEntityChanged(playlist);
            }
            catch (Exception ex)
            {
                _services.EventLogger.Report("Playlist Add Songs", ex);
            }
        }
    }
}
