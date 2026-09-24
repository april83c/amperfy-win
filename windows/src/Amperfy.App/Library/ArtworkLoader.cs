using Amperfy.App.Services;
using Amperfy.Core.Common;
using Amperfy.Core.Downloads;
using Amperfy.Core.Model;
using Microsoft.UI.Dispatching;

namespace Amperfy.App.Library;

/// Requests artwork downloads for displayed entities (Swift: LibraryEntityImage.displayAndUpdate).
/// Requests are collected and handed to the account's artwork download manager in batches; each
/// artwork is requested at most once per session. Visible images refresh when the download
/// finished (see LibraryEventHub).
public static class ArtworkLoader
{
    private static readonly HashSet<int> Requested = [];
    private static readonly List<Artwork> Pending = [];
    private static DispatcherQueueTimer? _timer;

    public static void Request(object? entity)
    {
        try
        {
            var services = AppServices.Instance;
            if (entity is null || services.Settings.User.IsOfflineMode) return;
            switch (entity)
            {
                case IPlayableContainable container:
                    var collection = container.GetArtworkCollection();
                    if (collection.QuadImageEntity is { Count: > 0 } quad)
                    {
                        foreach (var e in quad.Take(4)) Add(e);
                    }
                    else if (collection.SingleImageEntity is { } single)
                    {
                        Add(single);
                    }
                    break;
                case AbstractLibraryEntity libraryEntity:
                    Add(libraryEntity);
                    break;
            }
        }
        catch (Exception ex)
        {
            AmperfyLog.Error("ArtworkLoader", $"Artwork request failed: {ex.Message}");
        }
    }

    private static void Add(AbstractLibraryEntity entity)
    {
        if (entity.Artwork is not { } artwork) return;
        if (artwork.RelFilePath is not null && artwork.Status == ImageStatus.CustomImage) return;
        if (artwork.Status is not (ImageStatus.NotChecked or ImageStatus.FetchError)) return;
        if (!Requested.Add(artwork.Pk)) return;
        Pending.Add(artwork);
        EnsureTimer();
    }

    private static void EnsureTimer()
    {
        if (_timer is null)
        {
            if (DispatcherQueue.GetForCurrentThread() is not { } dispatcher)
            {
                Flush();
                return;
            }
            _timer = dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(250);
            _timer.IsRepeating = false;
            _timer.Tick += (_, _) => Flush();
        }
        if (!_timer.IsRunning) _timer.Start();
    }

    private static void Flush()
    {
        if (Pending.Count == 0) return;
        var batch = Pending.ToList();
        Pending.Clear();
        var services = AppServices.Instance;
        foreach (var group in batch.GroupBy(a => a.Account))
        {
            if (group.Key is not { } account) continue;
            try
            {
                services.Kit.GetMeta(account.Info).ArtworkDownloadManager.Download(group.Cast<IDownloadable>().ToList());
            }
            catch (Exception ex)
            {
                AmperfyLog.Error("ArtworkLoader", $"Artwork download request failed: {ex.Message}");
            }
        }
    }
}
