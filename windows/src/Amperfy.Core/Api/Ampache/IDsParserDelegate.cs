namespace Amperfy.Core.Api.Ampache;

/// First parser pass: collects only the ids of the elements (no storage access; may run on a
/// background thread). The ids are used to prefetch the existing entities in bulk.
public sealed class IDsParserDelegate : AmpacheNotifiableXmlParser
{
    public PrefetchIdContainer PrefetchIDs { get; } = new();

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        base.DidStartElement(elementName, attributes);
        if (!attributes.TryGetValue("id", out var id)) return;
        switch (elementName)
        {
            case "genre": PrefetchIDs.GenreIDs.Add(id); break;
            case "catalog": PrefetchIDs.MusicFolderIDs.Add(id); break;
            case "artist": PrefetchIDs.ArtistIDs.Add(id); break;
            case "album": PrefetchIDs.AlbumIDs.Add(id); break;
            case "song": PrefetchIDs.SongIDs.Add(id); break;
            case "podcast_episode": PrefetchIDs.PodcastEpisodeIDs.Add(id); break;
            case "live_stream": PrefetchIDs.RadioIDs.Add(id); break;
            case "podcast": PrefetchIDs.PodcastIDs.Add(id); break;
        }
    }

    protected override void DidEndElement(string elementName)
    {
        if (elementName == "art" && AmpacheXmlServerApi.ExtractArtworkInfoFromUrl(Buffer) is { } artworkRemoteInfo)
            PrefetchIDs.ArtworkIDs.Add(artworkRemoteInfo);
        base.DidEndElement(elementName);
    }
}
