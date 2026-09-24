namespace Amperfy.Core.Api.Ampache;

/// Parses Ampache catalogs into music folders. Folders that are no longer reported are deleted.
public sealed class CatalogParserDelegate : AmpacheXmlLibParser
{
    private readonly HashSet<MusicFolder> _musicFoldersBeforeFetch;
    public HashSet<MusicFolder> MusicFoldersParsed { get; } = [];
    private MusicFolder? _musicFolderBuffer;

    public CatalogParserDelegate(PrefetchElementContainer prefetch, Account account, LibraryStorage library)
        : base(prefetch, account, library)
    {
        _musicFoldersBeforeFetch = [.. library.GetMusicFolders(account)];
    }

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        base.DidStartElement(elementName, attributes);
        if (elementName != "catalog") return;
        if (!attributes.TryGetValue("id", out var id))
        {
            AmperfyLog.Error("Ampache", "Found catalog with no id");
            return;
        }
        if (Prefetch.PrefetchedMusicFolderDict.TryGetValue(id, out var prefetchedMusicFolder))
        {
            _musicFolderBuffer = prefetchedMusicFolder;
        }
        else
        {
            _musicFolderBuffer = Library.CreateMusicFolder(Account);
            _musicFolderBuffer.Id = id;
            Prefetch.PrefetchedMusicFolderDict[id] = _musicFolderBuffer;
        }
    }

    protected override void DidEndElement(string elementName)
    {
        switch (elementName)
        {
            case "name":
                if (_musicFolderBuffer is not null) _musicFolderBuffer.Name = Buffer;
                break;
            case "catalog":
                ParsedCount += 1;
                if (_musicFolderBuffer is { } parsedMusicFolder) MusicFoldersParsed.Add(parsedMusicFolder);
                _musicFolderBuffer = null;
                break;
            case "root":
                foreach (var removed in _musicFoldersBeforeFetch.Except(MusicFoldersParsed).ToList())
                    Library.DeleteMusicFolder(removed);
                break;
        }
        base.DidEndElement(elementName);
    }
}
