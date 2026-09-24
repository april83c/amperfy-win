using Amperfy.App.Helpers;
using Amperfy.App.Library;
using Amperfy.App.Services;
using Amperfy.Core.Downloads;
using Amperfy.Core.Model;
using Amperfy.Core.Player;
using Amperfy.Core.Storage;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Amperfy.App.Pages;

/// Download queue of the playable downloader (port of DownloadsVC): status of each download,
/// clear finished, retry failed, cancel all. Updates while downloads progress.
public sealed partial class DownloadsPage : Page, ILibraryCategoryPage
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly LibraryListContext _context = new() { IsEpisodeStyle = false, ShowAlbumInSubtitle = true };
    private readonly LibraryListController _controller;
    private readonly DispatcherQueueTimer _reloadTimer;
    private Account? _account;
    private DownloadManager? _downloadManager;
    private int _lastCount = -1;

    public LibraryDisplayType LibraryType { get; private set; } = LibraryDisplayType.Downloads;

    public DownloadsPage()
    {
        InitializeComponent();
        _context.HostPageType = typeof(DownloadsPage);
        _context.PlayContextProvider = item => item.Playable is { } playable ? new PlayContext(playable) : null;
        _controller = new LibraryListController(ItemsList, _context, LibraryListMode.Playables);
        Header.Title = "Downloads";
        Header.IsPlayVisible = false;
        Header.IsShuffleVisible = false;
        Header.IsFilterVisible = false;
        Header.AddCommand("Clear finished downloads", Icons.Clear, () => Run(m => m.ClearFinishedDownloads()));
        Header.AddCommand("Retry failed downloads", LibraryGlyphs.Retry, () => Run(m => m.ResetFailedDownloads()));
        Header.AddCommand("Cancel all downloads", Icons.Stop, () => Run(m => m.CancelDownloads()));
        Header.AttachAccelerators(this, Reload);
        _reloadTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _reloadTimer.Interval = TimeSpan.FromMilliseconds(700);
        _reloadTimer.IsRepeating = false;
        _reloadTimer.Tick += (_, _) => Update();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is LibraryDisplayType type) LibraryType = type;
        _account = _services.ActiveAccount;
        if (_account is not null)
        {
            _downloadManager = _services.Kit.GetMeta(_account.Info).PlayableDownloadManager;
            _downloadManager.DownloadsChanged += OnDownloadsChanged;
            _downloadManager.DownloadProgressChanged += OnDownloadProgressChanged;
        }
        LibraryEventHub.EnsureInitialized();
        LibraryEventHub.DownloadFinished += OnDownloadFinished;
        Reload();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_downloadManager is not null)
        {
            _downloadManager.DownloadsChanged -= OnDownloadsChanged;
            _downloadManager.DownloadProgressChanged -= OnDownloadProgressChanged;
        }
        LibraryEventHub.DownloadFinished -= OnDownloadFinished;
        _reloadTimer.Stop();
    }

    private void OnDownloadsChanged(object? sender, EventArgs e) => ScheduleReload();

    private void OnDownloadProgressChanged(object? sender, DownloadProgressEventArgs e) => ScheduleReload();

    private void OnDownloadFinished(string id) => ScheduleReload();

    private void ScheduleReload()
    {
        if (!_reloadTimer.IsRunning) _reloadTimer.Start();
    }

    private void Run(Action<DownloadManager> action)
    {
        if (_downloadManager is null) return;
        try { action(_downloadManager); }
        catch (Exception ex) { _services.EventLogger.Report("Downloads", ex); }
        Reload();
    }

    /// Reloads the list when downloads were added or removed; otherwise only refreshes the
    /// visible rows (keeps the scroll position while downloads progress).
    private void Update()
    {
        if (_account is null) return;
        var count = _services.Library.QueryDownloads(_account, DownloadableType.Playable).Count();
        if (count != _lastCount)
        {
            Reload();
            return;
        }
        UpdateInfo(_services.Library.QueryDownloads(_account, DownloadableType.Playable), count);
        LibraryEventHub.RaiseEntityChanged(this);
    }

    private void Reload()
    {
        if (_account is null) return;
        var query = _services.Library.QueryDownloads(_account, DownloadableType.Playable);
        var count = query.Count();
        _lastCount = count;
        _controller.SetIncrementalSource((skip, take) => query.Skip(skip).Take(take).ToList(), count);
        UpdateInfo(query, count);
        CategoryPageHelper.UpdateEmptyState(EmptyHost, count == 0, false, Icons.Download, "Downloads");
    }

    private void UpdateInfo(IQueryable<Download> query, int count)
    {
        var failed = query.Count(d => d.ErrorDate != null);
        var finished = query.Count(d => d.FinishDate != null && d.ErrorDate == null);
        var info = Ui.Plural(count, "Download", "Downloads");
        if (finished > 0) info += $"{LibraryText.Dot}{finished} finished";
        if (failed > 0) info += $"{LibraryText.Dot}{failed} failed";
        Header.Info = info;
    }
}
