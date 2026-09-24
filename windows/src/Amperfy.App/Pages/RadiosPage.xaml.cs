using Amperfy.App.Helpers;
using Amperfy.App.Library;
using Amperfy.App.Services;
using Amperfy.Core.Model;
using Amperfy.Core.Player;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Amperfy.App.Pages;

/// Internet radios (port of RadiosVC): play / shuffle, search, refresh from the server.
public sealed partial class RadiosPage : Page, ILibraryCategoryPage
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly LibraryListContext _context = new();
    private readonly LibraryListController _controller;
    private Account? _account;
    private List<Radio> _radios = [];

    public LibraryDisplayType LibraryType { get; private set; } = LibraryDisplayType.Radios;

    public RadiosPage()
    {
        InitializeComponent();
        _context.HostPageType = typeof(RadiosPage);
        _context.PlayContextProvider = item => new PlayContext("Radios", Math.Max(0, _radios.IndexOf((Radio)item.Playable!)), _radios.Cast<AbstractPlayable>().ToList());
        _controller = new LibraryListController(ItemsList, _context, LibraryListMode.Playables);
        Header.Title = "Radios";
        Header.FilterPlaceholder = "Search in \"Radios\"";
        Header.PlayRequested += () => PlayAll(random: false);
        Header.ShuffleRequested += () => PlayAll(random: true);
        Header.FilterChanged += (_, _) => Reload();
        Header.AttachAccelerators(this, () => _ = RefreshAsync());
        Header.AddCommand("Refresh", Icons.Refresh, () => _ = RefreshAsync(), "Refresh (F5)");
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is LibraryDisplayType type) LibraryType = type;
        _account = _services.ActiveAccount;
        LibraryEventHub.EnsureInitialized();
        LibraryEventHub.OfflineModeChanged += Reload;
        Reload();
        _ = SyncAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        LibraryEventHub.OfflineModeChanged -= Reload;
    }

    private void Reload()
    {
        if (_account is null) return;
        _radios = _services.Library.QuerySortedRadios(_account, Header.FilterText).ToList();
        _controller.SetItems(_radios);
        Header.Info = Ui.Plural(_radios.Count, "Radio", "Radios");
        Header.IsPlayEnabled = _radios.Count > 0 && _services.Settings.User.IsOnlineMode;
        CategoryPageHelper.UpdateEmptyState(EmptyHost, _radios.Count == 0, !string.IsNullOrEmpty(Header.FilterText), Icons.Radio, "Radios");
    }

    private void PlayAll(bool random)
    {
        var radios = _radios.Take(CategoryPageHelper.MaxSongsToAddOnce).Cast<AbstractPlayable>().ToList();
        if (radios.Count == 0) return;
        var index = random ? Random.Shared.Next(radios.Count) : 0;
        EntityActions.Play(new PlayContext("Radios", index, radios));
    }

    private async Task SyncAsync()
    {
        if (_account is not { } account) return;
        if (await CategoryPageHelper.SyncAsync("Radios Sync", () => EntityActions.SyncerFor(account).SyncRadiosAsync(), displayPopup: false))
            Reload();
    }

    private async Task RefreshAsync()
    {
        if (!_services.Settings.User.IsOnlineMode) return;
        Header.IsBusy = true;
        await SyncAsync();
        Header.IsBusy = false;
    }
}
