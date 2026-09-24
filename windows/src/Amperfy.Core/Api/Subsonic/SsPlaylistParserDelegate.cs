namespace Amperfy.Core.Api.Subsonic;

/// Parses the playlist list ("getPlaylists") without songs. Playlists which are no longer on the
/// server are deleted locally (except playlists without id = not yet uploaded).
public class SsPlaylistParserDelegate : SsXmlParser
{
    private Playlist? _playlist;
    private readonly HashSet<Playlist> _allOldPlaylists;
    private readonly Dictionary<string, Playlist> _playlistsDict = [];
    private readonly HashSet<Playlist> _parsedPlaylists = [];
    private readonly Account _account;
    private readonly LibraryStorage _library;

    public SsPlaylistParserDelegate(Account account, LibraryStorage library)
    {
        _account = account;
        _library = library;
        _allOldPlaylists = [.. library.GetPlaylists(account)];
        foreach (var pl in _allOldPlaylists) _playlistsDict[pl.Id] = pl;
    }

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        base.DidStartElement(elementName, attributes);
        if (elementName != "playlist") return;
        if (StrAttr(attributes, "id") is not { } playlistId || StrAttr(attributes, "name") is not { } attributePlaylistName) return;

        if (_playlist is not null)
        {
            _playlist.Id = playlistId;
        }
        else if (playlistId != "")
        {
            if (_playlistsDict.TryGetValue(playlistId, out var fetchedPlaylist))
            {
                _playlist = fetchedPlaylist;
            }
            else
            {
                _playlist = _library.CreatePlaylist(_account);
                _playlist.Id = playlistId;
                _playlistsDict[playlistId] = _playlist;
            }
        }
        else
        {
            AmperfyLog.Error("Parser", "Error: Playlist could not be parsed -> id is not given");
            return;
        }

        _playlist.Name = attributePlaylistName;
        if (SwiftInt(StrAttr(attributes, "duration")) is { } duration) _playlist.RemoteDuration = duration;
        if (SwiftInt(StrAttr(attributes, "songCount")) is { } songCount) _playlist.RemoteSongCount = songCount;
    }

    protected override void DidEndElement(string elementName)
    {
        switch (elementName)
        {
            case "playlist":
                ParsedCount += 1;
                if (_playlist is { } parsedPlaylist) _parsedPlaylists.Add(parsedPlaylist);
                _playlist = null;
                break;
            case "playlists":
                foreach (var outdated in _allOldPlaylists.Except(_parsedPlaylists).ToList())
                {
                    if (outdated.Id != "") _library.DeletePlaylist(outdated);
                }
                break;
        }
        base.DidEndElement(elementName);
    }
}
