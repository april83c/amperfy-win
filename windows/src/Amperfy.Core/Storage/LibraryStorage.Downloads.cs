using Amperfy.Core.Downloads;
using Microsoft.EntityFrameworkCore;

namespace Amperfy.Core.Storage;

/// Queries used by the download managers and the background syncers.
public sealed partial class LibraryStorage
{
    /// Downloads of an account for one downloadable type (Swift: DownloadMO.onlyPlayablesPredicate /
    /// onlyArtworksPredicate + account predicate), sorted by creation date and id
    /// (Swift: DownloadMO.creationDateSortedFetchRequest).
    public IQueryable<Model.Download> QueryDownloads(Account account, DownloadableType type)
    {
        var q = Context.Downloads.Where(d => d.AccountPk == account.Pk);
        q = type switch
        {
            DownloadableType.Playable => q.Where(d => d.PlayablePk != null),
            DownloadableType.Artwork => q.Where(d => d.ArtworkPk != null),
            _ => q.Where(d => d.PlayablePk == null && d.ArtworkPk == null),
        };
        return q.OrderBy(d => d.CreationDate).ThenBy(d => d.Id);
    }

    /// All downloads of the type (Downloads page list).
    public List<Model.Download> GetDownloads(Account account, DownloadableType type) => QueryDownloads(account, type).ToList();

    /// Downloads which are requested but not finished and not failed.
    public List<Model.Download> GetRequestedDownloads(Account account, DownloadableType type) =>
        QueryDownloads(account, type).Where(d => d.FinishDate == null && d.ErrorDate == null).ToList();

    /// Downloads which have finished (successfully or with an error).
    public List<Model.Download> GetFinishedOrFailedDownloads(Account account, DownloadableType type) =>
        QueryDownloads(account, type).Where(d => d.FinishDate != null || d.ErrorDate != null).ToList();

    public List<Model.Download> GetFailedDownloads(Account account, DownloadableType type) =>
        QueryDownloads(account, type).Where(d => d.ErrorDate != null).ToList();

    public int GetNotStartedDownloadCount(Account account, DownloadableType type) =>
        QueryDownloads(account, type).Count(d => d.StartDate == null);

    public int GetDownloadCount(Account account, DownloadableType type) => QueryDownloads(account, type).Count();

    /// True if the entity is still tracked and not deleted (e.g. a download removed while its transfer ran).
    public bool IsAlive(object entity)
    {
        var state = Context.Entry(entity).State;
        return state is not (EntityState.Deleted or EntityState.Detached);
    }

    /// Albums of the account whose songs have not been synced yet (Swift: getAlbumWithoutSyncedSongs,
    /// restricted to the account because each account has its own background syncer).
    public List<Album> GetAlbumsWithoutSyncedSongs(Account account) =>
        Context.Albums.Where(x => x.AccountPk == account.Pk && !x.IsSongsMetaDataSynced && x.RemoteStatus == RemoteStatus.Available).ToList();
}
