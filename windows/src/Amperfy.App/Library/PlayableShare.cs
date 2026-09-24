using Amperfy.App.Services;
using Amperfy.Core.Common;
using Amperfy.Core.Downloads;
using Amperfy.Core.Model;
using Amperfy.Core.Storage;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Amperfy.App.Library;

/// Sharing a song / podcast episode file (port of ShareSongAction): "Share…" opens the Windows share sheet,
/// "Save a Copy…" saves the file with a file dialog. Items that aren't cached are downloaded first.
public static class PlayableShare
{
    private const string Topic = "Share";
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(60);
    private static (StorageFile File, string Title)? _pendingShare;
    private static bool _isDataRequestedRegistered;

    private static AppServices Services => AppServices.Instance;

    /// Whether the file of a playable can be shared (Swift: isShareable). Radios are streams without a file.
    public static bool IsShareable(IPlayableContainable container) =>
        container is AbstractPlayable { IsRadio: false } playable && (playable.IsCached || Services.Settings.User.IsOnlineMode);

    public static async Task ShareAsync(AbstractPlayable playable)
    {
        try
        {
            if (await GetCachedFileAsync(playable) is not { } path) return;
            var tempPath = CopyToTemp(playable, path);
            var file = await StorageFile.GetFileFromPathAsync(tempPath);
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(Services.MainWindow);
            var manager = DataTransferManagerInterop.GetForWindow(hwnd);
            if (!_isDataRequestedRegistered)
            {
                manager.DataRequested += OnDataRequested;
                _isDataRequestedRegistered = true;
            }
            _pendingShare = (file, playable.DisplayString);
            DataTransferManagerInterop.ShowShareUIForWindow(hwnd);
        }
        catch (Exception ex)
        {
            Services.EventLogger.Report(Topic, ex);
        }
    }

    private static void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        if (_pendingShare is not { } share) return;
        _pendingShare = null;
        var data = args.Request.Data;
        data.Properties.Title = share.Title;
        data.SetText(share.Title);
        data.SetStorageItems([share.File]);
    }

    public static async Task SaveCopyAsync(AbstractPlayable playable)
    {
        try
        {
            if (await GetCachedFileAsync(playable) is not { } path) return;
            var extension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(extension)) extension = ".bin";
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary,
                SuggestedFileName = PlayableFileExport.SanitizedFileName(playable),
            };
            picker.FileTypeChoices.Add(extension.TrimStart('.').ToUpperInvariant() + " file", new List<string> { extension });
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(Services.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var target = await picker.PickSaveFileAsync();
            if (target is null) return;
            File.Copy(path, target.Path, overwrite: true);
            Services.Alerts.ShowInfo("Save a Copy", $"Saved to {target.Path}");
        }
        catch (Exception ex)
        {
            Services.EventLogger.Report("Save a Copy", ex);
        }
    }

    /// The absolute path of the cached file; downloads the playable first if needed (polls like the Swift app).
    private static async Task<string?> GetCachedFileAsync(AbstractPlayable playable)
    {
        if (CachedFilePath(playable) is { } cached) return cached;
        if (!Services.Settings.User.IsOnlineMode || playable.Account is not { } account) return null;
        Services.Alerts.ShowInfo("Downloading…", playable.DisplayString);
        EntityActions.DownloadManagerFor(account).Download(playable);
        var end = DateTime.UtcNow + DownloadTimeout;
        while (DateTime.UtcNow < end)
        {
            await Task.Delay(500);
            if (CachedFilePath(playable) is { } path) return path;
        }
        Services.EventLogger.Error(Topic, AmperfyLogStatusCode.DownloadError,
            "Download failed: the download took too long. Please try again.", displayPopup: true);
        return null;
    }

    private static string? CachedFilePath(AbstractPlayable playable)
    {
        if (playable.RelFilePath is not { } relPath) return null;
        var path = CacheFileManager.Shared.GetAbsolutePath(relPath);
        return File.Exists(path) ? path : null;
    }

    /// Copy with a readable file name ("Artist - Title.ext") for the share target.
    private static string CopyToTemp(AbstractPlayable playable, string path)
    {
        var dir = Path.Combine(Path.GetTempPath(), "Amperfy", "Share");
        Directory.CreateDirectory(dir);
        var tempPath = Path.Combine(dir, PlayableFileExport.ExportFileName(playable, path));
        File.Copy(path, tempPath, overwrite: true);
        return tempPath;
    }
}
