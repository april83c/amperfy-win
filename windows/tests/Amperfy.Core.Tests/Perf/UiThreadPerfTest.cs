using System.Text;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Amperfy.Core.Tests.Perf;

/// Measures storage work that runs on the UI thread with a large library (opt-in: AMPERFY_PERF=1).
[Collection("Integration")]
public class UiThreadPerfTest(ITestOutputHelper output)
{
    private const int ArtistCount = 400;
    private const int AlbumCount = 2500;
    private const int SongCount = 25000;
    private const int PlaylistSize = 1300;

    private static bool Enabled => Environment.GetEnvironmentVariable("AMPERFY_PERF") == "1";

    private static string Seed(string dir)
    {
        using var storage = PersistentStorage.Open(dir);
        var library = storage.Main;
        var account = library.GetAccount(AccountInfo.Create("https://perf.example", "perf", BackendApiType.Subsonic));
        var rnd = new Random(1);
        var artists = Enumerable.Range(0, ArtistCount).Select(i =>
        {
            var a = library.CreateArtist(account);
            a.Id = $"ar-{i}"; a.Name = $"Artist {i}";
            var art = library.CreateArtwork(account); art.Id = $"ar-{i}"; art.Type = ""; a.Artwork = art;
            return a;
        }).ToList();
        var albums = Enumerable.Range(0, AlbumCount).Select(i =>
        {
            var al = library.CreateAlbum(account);
            al.Id = $"al-{i}"; al.Name = $"Album {i}"; al.Artist = artists[i % ArtistCount];
            var art = library.CreateArtwork(account); art.Id = $"al-{i}"; art.Type = ""; al.Artwork = art;
            return al;
        }).ToList();
        var songs = Enumerable.Range(0, SongCount).Select(i =>
        {
            var s = library.CreateSong(account);
            var album = albums[i % AlbumCount];
            s.Id = $"so-{i}"; s.Title = $"Song {i}"; s.Album = album; s.Artist = album.Artist; s.Artwork = album.Artwork;
            s.CombinedDuration = 180 + i % 120; s.Track = i / AlbumCount + 1;
            return s;
        }).ToList();
        library.SaveContext();
        var playlist = library.CreatePlaylist(account);
        playlist.Id = "pl-1"; playlist.Name = "Big playlist";
        playlist.Append(Enumerable.Range(0, PlaylistSize).Select(_ => songs[rnd.Next(SongCount)]).Cast<AbstractPlayable>().ToList());
        var playlist2 = library.CreatePlaylist(account);
        playlist2.Id = "pl-2"; playlist2.Name = "Second playlist";
        playlist2.Append(Enumerable.Range(0, PlaylistSize).Select(_ => songs[rnd.Next(SongCount)]).Cast<AbstractPlayable>().ToList());
        library.SaveContext();
        return account.Info.Ident;
    }

    private void Measure(string name, Action action)
    {
        var sw = Stopwatch.StartNew();
        action();
        output.WriteLine($"{name,-60} {sw.ElapsedMilliseconds,7} ms");
    }

    [Fact]
    public void LargeLibrary()
    {
        if (!Enabled) return;
        var dir = Path.Combine(Path.GetTempPath(), "amperfy-perf", Guid.NewGuid().ToString("N"));
        CacheFileManager.Shared = new CacheFileManager(Path.Combine(dir, "cache"));
        var sw = Stopwatch.StartNew();
        var ident = Seed(dir);
        output.WriteLine($"seed: {sw.ElapsedMilliseconds} ms");

        using var storage = PersistentStorage.Open(dir); // cold start: nothing tracked
        var library = storage.Main;
        var account = library.GetAccount(AccountInfo.Create(ident)!);
        Playlist playlist = null!;
        List<PlaylistItem> items = null!;
        Measure("playlist lookup", () => playlist = library.GetPlaylists(account).First(p => p.Id == "pl-1"));
        Measure("items query incl. playable", () =>
        {
            var entry = library.Context.Entry(playlist).Collection(p => p.ItemsRaw);
            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.Load(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.Include(entry.Query(), i => i.Playable));
        });
        Measure("items query again (already tracked)", () =>
        {
            var entry = library.Context.Entry(playlist).Collection(p => p.ItemsRaw);
            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.Load(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.Include(entry.Query(), i => i.Playable));
        });
        Measure("LoadPlaylistItems (batch display data)", () => library.LoadPlaylistItems(playlist));
        Measure("playlist.Items (1300)", () => items = playlist.Items.ToList());
        Measure("item.Playable + title (lazy load per item)", () => { foreach (var i in items) _ = i.Playable?.Title; });
        Measure("row data: artist/album/cached/fav/duration", () =>
        {
            foreach (var i in items)
            {
                var p = i.Playable!;
                _ = (p as Song)?.Artist?.Name; _ = (p as Song)?.Album?.Name; _ = p.IsCached; _ = p.IsFavorite; _ = p.Duration;
            }
        });
        Measure("artwork: ImagePath(PreferId3Tag) + collection + status", () =>
        {
            foreach (var i in items)
            {
                var p = i.Playable!;
                _ = p.ImagePath(ArtworkDisplayPreference.PreferId3Tag);
                _ = p.GetArtworkCollection();
                _ = p.Artwork?.Status;
            }
        });
        output.WriteLine($"tracked entities: {library.Context.ChangeTracker.Entries().Count()}");
        Measure("SaveContext (no changes)", library.SaveContext);
        // playlist sync from the server (same songs): getPlaylist response
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><subsonic-response xmlns=\"http://subsonic.org/restapi\" status=\"ok\" version=\"1.16.1\">");
        xml.Append($"<playlist id=\"{playlist.Id}\" name=\"Big playlist\" songCount=\"{items.Count}\" duration=\"1000\">");
        foreach (var i in items)
        {
            var song = (Song)i.Playable!;
            xml.Append($"<entry id=\"{song.Id}\" parent=\"{song.Album!.Id}\" title=\"{song.Title}\" album=\"{song.Album.Name}\" artist=\"{song.Artist!.Name}\" isDir=\"false\" coverArt=\"{song.Album.Id}\" duration=\"{song.Duration}\" track=\"{song.Track}\" albumId=\"{song.Album.Id}\" artistId=\"{song.Artist.Id}\" type=\"music\" suffix=\"mp3\" contentType=\"audio/mpeg\" size=\"1000\" bitRate=\"128\"/>");
        }
        xml.Append("</playlist></subsonic-response>");
        var data = Encoding.UTF8.GetBytes(xml.ToString());
        PrefetchIdContainer ids = null!;
        Measure("sync: ids parser (background thread in app)", () =>
        {
            var idParser = new Amperfy.Core.Api.Subsonic.SsIDsParserDelegate();
            idParser.Parse(data);
            ids = idParser.PrefetchIDs;
        });
        PrefetchElementContainer prefetch = null!;
        Measure("sync: prefetch GetElements (UI thread)", () => prefetch = library.GetElements(account, ids));
        var countBefore = library.Context.ChangeTracker.Entries().Count();
        Measure("sync: playlist parser (UI thread)", () =>
        {
            var parser = new Amperfy.Core.Api.Subsonic.SsPlaylistSongsParserDelegate(playlist, account, library, prefetch);
            parser.Parse(data);
        });
        foreach (var g in library.Context.ChangeTracker.Entries().Where(e => e.State != Microsoft.EntityFrameworkCore.EntityState.Unchanged).GroupBy(e => (e.Entity.GetType().BaseType!.Name, e.State)))
        {
            var first = g.First();
            var modified = first.State == Microsoft.EntityFrameworkCore.EntityState.Modified ? string.Join(",", first.Properties.Where(p => p.IsModified).Select(p => $"{p.Metadata.Name}:{p.OriginalValue}->{p.CurrentValue}")) : "";
            output.WriteLine($"  {g.Key}: {g.Count()} {modified}");
        }
        output.WriteLine($"entities added by sync: {library.Context.ChangeTracker.Entries().Count() - countBefore}, modified: {library.Context.ChangeTracker.Entries().Count(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Modified)}");
        Measure("sync: save", library.SaveContext);
        Measure("sync again: prefetch + parser + save", () =>
        {
            var p2 = library.GetElements(account, ids);
            var parser = new Amperfy.Core.Api.Subsonic.SsPlaylistSongsParserDelegate(playlist, account, library, p2);
            parser.Parse(data);
            library.SaveContext();
        });
        Measure("50x (artwork status change + SaveContext)", () =>
        {
            foreach (var i in items.Take(50))
            {
                if (i.Playable!.Artwork is { } a) { a.Status = ImageStatus.CustomImage; a.RelFilePath = $"x/{a.Pk}.jpg"; }
                library.SaveContext();
            }
        });
        Measure("songs page: 10 pages of 100 + LoadDisplayData", () =>
        {
            var query = library.QuerySongs(account, "", false, DisplayCategoryFilter.All);
            for (var page = 0; page < 10; page++)
            {
                var songs = query.Skip(page * 100).Take(100).ToList();
                library.LoadDisplayData(songs);
                foreach (var s in songs) { _ = s.Album?.Name; _ = s.Artist?.Name; _ = s.ImagePath(ArtworkDisplayPreference.PreferId3Tag); }
            }
        });
        var second = library.GetPlaylists(account).First(p => p.Id == "pl-2");
        Measure("second playlist: LoadPlaylistItems (queries compiled)", () => library.LoadPlaylistItems(second));
        Measure("second playlist: row data + artwork", () =>
        {
            foreach (var i in second.Items)
            {
                var p = i.Playable!;
                _ = (p as Song)?.Artist?.Name; _ = (p as Song)?.Album?.Name; _ = p.ImagePath(ArtworkDisplayPreference.PreferId3Tag); _ = p.Artwork?.Status;
            }
        });
        Measure("load all songs (unpaged)", () => _ = library.GetSongs(account).Count);
        output.WriteLine($"tracked entities: {library.Context.ChangeTracker.Entries().Count()}");
        Measure("SaveContext (no changes) with all songs tracked", library.SaveContext);
        Measure("10x SaveContext with all songs tracked", () => { for (var i = 0; i < 10; i++) library.SaveContext(); });
    }
}
