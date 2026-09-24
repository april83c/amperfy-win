namespace Amperfy.Core.Api.Subsonic;

/// Parses a music directory ("getMusicDirectory") or the indexes of a music folder ("getIndexes").
public class SsDirectoryParserDelegate : SsSongParserDelegate
{
    private readonly MusicDirectory? _directory;
    private readonly MusicFolder? _musicFolder;

    private readonly HashSet<MusicDirectory> _directoriesBeforeFetch;
    private readonly HashSet<MusicDirectory> _directoriesParsed = [];
    private readonly HashSet<Song> _songsBeforeFetch;
    private readonly HashSet<Song> _songsParsed = [];

    public SsDirectoryParserDelegate(MusicDirectory directory, PrefetchElementContainer prefetch, Account account, LibraryStorage library)
        : base(prefetch, account, library)
    {
        _directory = directory;
        _musicFolder = null;
        _directoriesBeforeFetch = [.. directory.SubdirectoriesRaw];
        _songsBeforeFetch = [.. directory.SongsRaw];
    }

    public SsDirectoryParserDelegate(MusicFolder musicFolder, PrefetchElementContainer prefetch, Account account, LibraryStorage library)
        : base(prefetch, account, library)
    {
        _directory = null;
        _musicFolder = musicFolder;
        _directoriesBeforeFetch = [.. musicFolder.DirectoriesRaw];
        _songsBeforeFetch = [.. musicFolder.SongsRaw];
    }

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        base.DidStartElement(elementName, attributes);

        if (elementName == "child")
        {
            if (SwiftBool(StrAttr(attributes, "isDir")) == true)
            {
                if (StrAttr(attributes, "id") is { } id && StrAttr(attributes, "title") is { } title)
                {
                    if (!Prefetch.PrefetchedDirectoryDict.TryGetValue(id, out var parsedDirectory))
                    {
                        parsedDirectory = Library.CreateDirectory(Account);
                        Prefetch.PrefetchedDirectoryDict[id] = parsedDirectory;
                        parsedDirectory.Id = id;
                        parsedDirectory.Name = title;
                        if (StrAttr(attributes, "coverArt") is { } coverArtId) parsedDirectory.Artwork = ParseArtwork(coverArtId);
                    }
                    AddDirectory(parsedDirectory);
                }
            }
            else if (SongBuffer is { } song)
            {
                if (_directory is { } directory)
                {
                    directory.SongsRaw.Add(song);
                    _songsParsed.Add(song);
                }
                else if (_musicFolder is { } musicFolder)
                {
                    musicFolder.SongsRaw.Add(song);
                    _songsParsed.Add(song);
                }
            }
        }
        if (elementName == "artist")
        {
            if (StrAttr(attributes, "id") is { } id && StrAttr(attributes, "name") is { } name)
            {
                if (!Prefetch.PrefetchedDirectoryDict.TryGetValue(id, out var parsedDirectory))
                {
                    parsedDirectory = Library.CreateDirectory(Account);
                    Prefetch.PrefetchedDirectoryDict[id] = parsedDirectory;
                    parsedDirectory.Id = id;
                    parsedDirectory.Name = name;
                }
                AddDirectory(parsedDirectory);
            }
        }
    }

    private void AddDirectory(MusicDirectory parsedDirectory)
    {
        if (_directory is { } directory) directory.SubdirectoriesRaw.Add(parsedDirectory);
        else if (_musicFolder is { } musicFolder) musicFolder.DirectoriesRaw.Add(parsedDirectory);
        _directoriesParsed.Add(parsedDirectory);
    }

    protected override void DidEndElement(string elementName)
    {
        if (elementName is "indexes" or "directory")
        {
            foreach (var removed in _directoriesBeforeFetch.Except(_directoriesParsed).ToList())
            {
                Library.DeleteDirectory(removed);
            }

            if (_directory is { } directory)
            {
                foreach (var removed in _songsBeforeFetch.Except(_songsParsed).ToList()) directory.SongsRaw.Remove(removed);
                directory.IsCached = IsCollectionCached;
            }
            else if (_musicFolder is { } musicFolder)
            {
                foreach (var removed in _songsBeforeFetch.Except(_songsParsed).ToList()) musicFolder.SongsRaw.Remove(removed);
                musicFolder.IsCached = IsCollectionCached;
            }
        }
        base.DidEndElement(elementName);
    }
}
