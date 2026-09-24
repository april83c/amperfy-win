namespace Amperfy.Core.Downloads;

/// A queued download (port of DownloadProtocols.swift DownloadRequest). Identity is the unique id of
/// the downloadable element (<see cref="DownloadableExtensions.UniqueId"/>).
public sealed class DownloadRequest : IEquatable<DownloadRequest>
{
    public string Id { get; }
    public string Title { get; }
    public Model.Download Download { get; }
    public IDownloadable Element { get; }

    public DownloadRequest(string id, string title, Model.Download download, IDownloadable element)
    {
        Id = id;
        Title = title;
        Download = download;
        Element = element;
    }

    public bool Equals(DownloadRequest? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as DownloadRequest);
    public override int GetHashCode() => Id.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => $"DownloadRequest({Id}: {Title})";
}

/// Creates and tracks the <see cref="Model.Download"/> rows of one download manager
/// (port of DownloadRequestManager.swift). Runs on the main thread.
public sealed class DownloadRequestManager
{
    private readonly Account _account;
    private readonly LibraryStorage _library;

    /// Type of downloads handled (Swift: DownloadManagerDelegate.requestPredicate).
    public DownloadableType DownloadType { get; }

    public DownloadRequestManager(Account account, LibraryStorage library, DownloadableType downloadType)
    {
        _account = account;
        _library = library;
        DownloadType = downloadType;
    }

    private static DownloadRequest CreateRequest(Model.Download download, IDownloadable element) =>
        new(element.UniqueId(), download.Title, download, element);

    /// Adds a single element. Existing downloads are reset and returned only for playables
    /// (when failed or not cached); existing artwork downloads are ignored.
    public DownloadRequest? Add(IDownloadable obj)
    {
        var id = obj.UniqueId();
        var existing = _library.GetDownload(_account, id);
        if (existing is not null)
        {
            if (obj.DownloadableType == DownloadableType.Playable && (existing.ErrorDate is not null || !obj.IsCached))
            {
                existing.Reset();
                _library.SaveContext();
                return CreateRequest(existing, obj);
            }
            return null;
        }
        var newDownload = _library.CreateDownload(_account, id);
        newDownload.Element = obj;
        _library.SaveContext();
        return CreateRequest(newDownload, obj);
    }

    /// Adds several elements. Existing downloads are reset when failed or not cached.
    public List<DownloadRequest> Add(IReadOnlyCollection<IDownloadable> objects)
    {
        var result = new List<DownloadRequest>();
        if (objects.Count == 0) return result;
        var hasChanges = false;
        var ids = objects.Select(o => o.UniqueId()).ToHashSet();
        var existingDict = _library.GetDownloadsDict(_account, ids);
        // also consider not yet saved downloads
        foreach (var local in _library.Context.Downloads.Local.Where(d => d.AccountPk == _account.Pk || d.Account == _account))
        {
            if (ids.Contains(local.Id) && _library.IsAlive(local)) existingDict.TryAdd(local.Id, local);
        }
        var handled = new HashSet<string>();
        foreach (var obj in objects)
        {
            var id = obj.UniqueId();
            if (!handled.Add(id)) continue;
            if (existingDict.TryGetValue(id, out var existing))
            {
                if (existing.ErrorDate is not null || !obj.IsCached)
                {
                    existing.Reset();
                    hasChanges = true;
                    result.Add(CreateRequest(existing, obj));
                }
                // else: the download has already an unused request
            }
            else
            {
                hasChanges = true;
                var newDownload = _library.CreateDownload(_account, id);
                newDownload.Element = obj;
                result.Add(CreateRequest(newDownload, obj));
            }
        }
        if (hasChanges) _library.SaveContext();
        return result;
    }

    public void RemoveFinishedDownload(string uniqueId)
    {
        var existing = _library.GetDownload(_account, uniqueId);
        if (existing is null || existing.FinishDate is null) return;
        _library.DeleteDownload(existing);
        _library.SaveContext();
    }

    public void RemoveFinishedDownload(IEnumerable<string> uniqueIds)
    {
        foreach (var uniqueId in uniqueIds)
        {
            var existing = _library.GetDownload(_account, uniqueId);
            if (existing is null || existing.FinishDate is null) continue;
            _library.DeleteDownload(existing);
        }
        _library.SaveContext();
    }

    /// Requested (not finished, not failed) downloads. The start date is reset for all of them
    /// except the ones in <paramref name="activeIds"/> (currently transferring).
    public List<DownloadRequest> GetRequestedDownloads(IReadOnlySet<string>? activeIds = null)
    {
        var result = new List<DownloadRequest>();
        foreach (var download in _library.GetRequestedDownloads(_account, DownloadType))
        {
            if (activeIds is null || !activeIds.Contains(download.Id)) download.StartDate = null;
            if (download.Element is not { } element) continue;
            result.Add(CreateRequest(download, element));
        }
        _library.SaveContext();
        return result;
    }

    /// Deletes all finished and all failed downloads.
    /// Note: Swift combined the account predicate with OR (deleting every download of the account);
    /// the intended semantics (finished or failed downloads of this account) are implemented here.
    public void ClearFinishedDownloads()
    {
        foreach (var download in _library.GetFinishedOrFailedDownloads(_account, DownloadType))
            _library.DeleteDownload(download);
        _library.SaveContext();
    }

    /// Deletes all downloads if none of them is waiting to be started.
    public void ClearAllDownloadsIfAllHaveFinished()
    {
        if (_library.GetNotStartedDownloadCount(_account, DownloadType) == 0) ClearAllDownloads();
    }

    public void ClearAllDownloads()
    {
        foreach (var download in _library.GetDownloads(_account, DownloadType))
            _library.DeleteDownload(download);
        _library.SaveContext();
    }

    /// Marks all pending downloads as canceled.
    public void CancelDownloads()
    {
        foreach (var download in _library.GetRequestedDownloads(_account, DownloadType))
            download.Error = DownloadError.Canceled;
        _library.SaveContext();
    }

    /// Resets failed downloads and returns them as new requests.
    public List<DownloadRequest> GetAndResetFailedDownloads()
    {
        var result = new List<DownloadRequest>();
        foreach (var download in _library.GetFailedDownloads(_account, DownloadType))
        {
            download.Reset();
            if (download.Element is not { } element) continue;
            result.Add(CreateRequest(download, element));
        }
        _library.SaveContext();
        return result;
    }
}
