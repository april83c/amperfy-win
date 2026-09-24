namespace Amperfy.Core.Api.Subsonic;

public class SsMusicFolderParserDelegate : SsXmlLibParser
{
    private readonly HashSet<MusicFolder> _musicFoldersBeforeFetch;
    private readonly Dictionary<string, MusicFolder> _musicFoldersDict = [];
    private readonly HashSet<MusicFolder> _musicFoldersParsed = [];

    public SsMusicFolderParserDelegate(PrefetchElementContainer prefetch, Account account, LibraryStorage library)
        : base(prefetch, account, library)
    {
        _musicFoldersBeforeFetch = [.. library.GetMusicFolders(account)];
        foreach (var mf in _musicFoldersBeforeFetch) _musicFoldersDict[mf.Id] = mf;
    }

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        base.DidStartElement(elementName, attributes);
        if (elementName == "musicFolder" && StrAttr(attributes, "id") is { } id && StrAttr(attributes, "name") is { } name)
        {
            if (_musicFoldersDict.TryGetValue(id, out var musicFolder))
            {
                _musicFoldersParsed.Add(musicFolder);
            }
            else
            {
                musicFolder = Library.CreateMusicFolder(Account);
                musicFolder.Id = id;
                musicFolder.Name = name;
                _musicFoldersDict[id] = musicFolder;
                _musicFoldersParsed.Add(musicFolder);
            }
        }
    }

    protected override void DidEndElement(string elementName)
    {
        if (elementName == "musicFolders")
        {
            foreach (var removed in _musicFoldersBeforeFetch.Except(_musicFoldersParsed).ToList())
            {
                Library.DeleteMusicFolder(removed);
            }
        }
        base.DidEndElement(elementName);
    }
}
