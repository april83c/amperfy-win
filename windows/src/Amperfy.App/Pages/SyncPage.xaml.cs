using Amperfy.App.Services;
using Amperfy.Core.Api;
using Amperfy.Core.Model;
using Amperfy.Core.Storage;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Amperfy.App.Pages;

/// Initial library sync with progress (port of SyncVC).
public sealed partial class SyncPage : Page, ISyncCallbacks
{
    private readonly AppServices _services = AppServices.Instance;
    private Account? _account;
    private bool _isDone;
    private int _total;
    private int _parsed;
    private ParsedObjectType _currentType;
    private DispatcherQueue _dispatcher = null!;

    public SyncPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _dispatcher = DispatcherQueue;
        _account = e.Parameter as Account ?? _services.ActiveAccount;
        if (_account is null)
        {
            _services.MainWindow.ShowLogin();
            return;
        }
        var status = await _services.Kit.SyncInitialAsync(_account, this);
        if (_isDone) return;
        _isDone = true;
        if (status == SyncCompletionStatus.Aborted)
        {
            await _services.Dialogs.ShowMessageAsync("Initial sync failed",
                "The library could not be synced completely. Amperfy continues with the synced part; you can resync the library in Settings.");
        }
        _services.MainWindow.ShowShell();
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDone || _account is null) return;
        _isDone = true;
        _services.Kit.SkipInitialSync(_account.Info);
        _services.MainWindow.ShowShell();
    }

    private static string Describe(ParsedObjectType type) => type switch
    {
        ParsedObjectType.Artist => "Artists",
        ParsedObjectType.Album => "Albums",
        ParsedObjectType.Song => "Songs",
        ParsedObjectType.Playlist => "Playlists",
        ParsedObjectType.Genre => "Genres",
        ParsedObjectType.Podcast => "Podcasts",
        ParsedObjectType.Cache => "Cache",
        _ => type.ToString(),
    };

    public void NotifySyncStarted(ParsedObjectType parsedObjectType, int totalCount) => _dispatcher.TryEnqueue(() =>
    {
        _currentType = parsedObjectType;
        _total = totalCount;
        _parsed = 0;
        StatusText.Text = $"Syncing {Describe(parsedObjectType)}…";
        Progress.IsIndeterminate = totalCount <= 0;
        Progress.Maximum = Math.Max(1, totalCount);
        Progress.Value = 0;
        CountText.Text = "";
    });

    public void NotifyParsedObject(ParsedObjectType parsedObjectType) => _dispatcher.TryEnqueue(() =>
    {
        if (parsedObjectType != _currentType) return;
        _parsed++;
        if (_total > 0)
        {
            Progress.Value = Math.Min(_parsed, _total);
            CountText.Text = $"{Math.Min(_parsed, _total)} / {_total}";
        }
        else
        {
            CountText.Text = $"{_parsed}";
        }
    });
}
