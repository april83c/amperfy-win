using Amperfy.Core.Api;
using Amperfy.Core.Model;
using Amperfy.Core.Storage;

namespace Amperfy.App.Library;

/// A multi selection of playables in a list, usable as a context menu container (play, queue,
/// add to playlist, download, delete cache).
public sealed class PlayableSelection : IPlayableContainable
{
    public PlayableSelection(IReadOnlyList<AbstractPlayable> playables, string name = "Selection")
    {
        Playables = playables;
        Name = name;
    }

    public string Id => "";
    public string Name { get; }
    public string? Subtitle => null;
    public string? Subsubtitle => null;
    public List<string> InfoDetails(ServerApiType? api, DetailInfoType details) => [Ui.Plural(Playables.Count, "Item", "Items")];
    public IReadOnlyList<AbstractPlayable> Playables { get; }
    public PlayerMode PlayContextType => Playables.Count > 0 && Playables.All(p => p.IsPodcastEpisode) ? PlayerMode.Podcast : PlayerMode.Music;
    public Account? Account => Playables.FirstOrDefault()?.Account;
    public bool IsRateable => false;
    public bool IsFavoritable => false;
    public bool IsFavorite => false;
    public bool IsDownloadAvailable => true;
    public ArtworkCollection GetArtworkCollection() => new(ArtworkType.Song, Playables.FirstOrDefault());
    public Task FetchFromServerAsync(ILibrarySyncer librarySyncer) => Task.CompletedTask;
    public Task RemoteToggleFavoriteAsync(LibraryStorage library, ILibrarySyncer syncer) => Task.CompletedTask;
    public void PlayedViaContext() { }
    public PlayableContainerIdentifier ContainerIdentifier => new(null, null);
}
