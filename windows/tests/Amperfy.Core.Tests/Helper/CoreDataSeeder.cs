namespace Amperfy.Core.Tests.Helper;

/// Port of CoreDataSeeder.swift (same data set).
public sealed class CoreDataSeeder
{
    public readonly (string ServerHash, string UserHash, BackendApiType ApiType)[] Accounts =
    [
        (TestAccountInfo.Test1ServerHash, TestAccountInfo.Test1UserHash, BackendApiType.Ampache),
        (TestAccountInfo.Test2ServerHash, TestAccountInfo.Test2UserHash, BackendApiType.Subsonic),
    ];

    public readonly (int AccountIndex, string Id, string Name)[] Artists =
    [
        (0, "4", "My Dream"),
        (0, "RopLcTz92", "She or He"),
        (0, "93", "Bang!"),
        (1, "4", "My Dream acc2"),
        (1, "Acc2Artist", "Acc 2 Artist"),
    ];

    public readonly (int AccountIndex, string Id, string ArtistId, string Name, int Year)[] Albums =
    [
        (0, "12", "4", "High Voltage", 2018),
        (0, "34", "RopLcTz92", "Du Hast", 1987),
        (0, "59", "93", "Dreams", 2002),
        (0, "6BTR0", "93", "Let it go", 2007),
        (1, "12", "4", "High Voltage acc2", 2018),
        (1, "acc2", "acc2Album", "Dreams for acc 2", 2002),
    ];

    public readonly (int AccountIndex, string Id, string ArtistId, string AlbumId, int Track, bool IsCached, string Title, string Url)[] Songs =
    [
        (0, "3", "4", "12", 3, false, "go home", "www.blub.de/ahhh"),
        (0, "5", "4", "12", 4, false, "well", "www.blub.de/ahhh2"),
        (0, "10T", "4", "12", 8, false, "maybe alright", "www.blub.de/dd"),
        (0, "19", "RopLcTz92", "34", 0, false, "baby", "www.blub.de/dddtd"),
        (0, "36", "RopLcTz92", "34", 1, true, "son", "www.blub.de/dddtdiuz"),
        (0, "38", "93", "59", 4, true, "oh no", "www.blub.de/dddtd23iuz"),
        (0, "41", "93", "59", 5, true, "please", "www.blub.de/dddtd233iuz"),
        (0, "54", "93", "6BTR0", 1, true, "see", "www.blub.de/ddf"),
        (0, "55", "93", "6BTR0", 2, true, "feel", "www.blub.de/654"),
        (0, "56", "93", "6BTR0", 3, true, "house", "www.blub.de/trd"),
        (0, "57", "93", "6BTR0", 4, true, "car", "www.blub.de/jhrf"),
        (0, "59", "asd", "6BTR0", 7, true, "vllll", "www.blub.de/jads324hrf"),
        (0, "99", "asd", "6BTR0", 8, true, "cllll", "www.blub.de/jds32s4hrf"),
        (0, "5a9", "asd", "6BTR0", 10, true, "allll", "www.blub.de/sjds324hrf"),
        (0, "59e", "asd", "6BTR0", 158, true, "3llll", "www.blub.de/gjds324hrf"),
        (0, "5e9lll", "asd", "6BTR0", 198, true, "3lldll", "www.blub.de/aajds324hrf"),
        // Account 2
        (1, "3", "4", "12", 3, false, "go home acc2", "www.blub.de/ahhh"),
        (1, "5", "4", "12", 4, false, "well acc2", "www.blub.de/ahhh2"),
        (1, "10T", "4", "12", 8, false, "maybe alright acc2", "www.blub.de/dd"),
        (1, "acc2Song", "Acc2Artist", "acc2", 0, false, "baby acc2", "www.blub.de/dddtd"),
    ];

    public readonly (int AccountIndex, string Id, string Name, string[] SongIds)[] Playlists =
    [
        (0, "3", "With One Cached", ["3", "5", "10T", "36", "19"]),
        (0, "9", "With Three Cached", ["3", "5", "10T", "36", "19", "10T", "38", "5", "41"]),
        (0, "dRsa11", "No Cached", ["3", "10T", "19"]),
        (0, "d23884", "All Cached", ["99", "5a9", "59e", "5e9lll"]),
        (1, "3", "With One Cached for Acc2", ["3", "5", "10T", "acc2Song"]),
        (1, "acc2playlist", "With Three Cached", ["acc2Song", "5", "10T", "acc2Song"]),
    ];

    public readonly (int AccountIndex, string Id, string Title, string Url, string SiteUrl)[] Radios =
    [
        (0, "12", "GoGo Radio", "www.blub.de/aaa", "www.blub.de/ddf"),
        (0, "36", "Wau", "www.blub.de/fjjuf", "www.blub.de/23452"),
        (0, "dRsa11", "Invalid Url", "", "www.blub.de/daeeaa"),
        (0, "dFrDF", "Radio Channel 2", "www.blub.de/ffhnnnza", "www.blub.de/44re4t"),
        (1, "12", "GoGo Radio Acc 2", "www.blub.de/aaa", "www.blub.de/ddf"),
        (1, "3445", "WauWau", "www.blub.de/fjjssuf", "www.blub.de/2s3452"),
    ];

    public const string CachedRelFilePath = "testSong";

    public void Seed(LibraryStorage library)
    {
        var accs = Accounts.Select(a => library.CreateAccount(new AccountInfo(a.ServerHash, a.UserHash, a.ApiType))).ToList();
        library.SaveContext();
        foreach (var seed in Artists)
        {
            var artist = library.CreateArtist(accs[seed.AccountIndex]);
            artist.Id = seed.Id;
            artist.Name = seed.Name;
        }
        library.SaveContext();
        foreach (var seed in Albums)
        {
            var album = library.CreateAlbum(accs[seed.AccountIndex]);
            album.Id = seed.Id;
            album.Name = seed.Name;
            album.Year = seed.Year;
            album.Artist = library.GetArtist(accs[seed.AccountIndex], seed.ArtistId);
        }
        library.SaveContext();
        var absFilePath = CacheFileManager.Shared.GetAbsolutePath(CachedRelFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absFilePath)!);
        File.WriteAllBytes(absFilePath, Convert.FromBase64String("Test"));
        foreach (var seed in Songs)
        {
            var song = library.CreateSong(accs[seed.AccountIndex]);
            song.Id = seed.Id;
            song.Title = seed.Title;
            song.Track = seed.Track;
            song.Url = seed.Url;
            song.Artist = library.GetArtist(accs[seed.AccountIndex], seed.ArtistId);
            song.Album = library.GetAlbum(accs[seed.AccountIndex], seed.AlbumId);
            if (seed.IsCached) song.RelFilePath = CachedRelFilePath;
            library.SaveContext();
        }
        foreach (var seed in Playlists)
        {
            var playlist = library.CreatePlaylist(accs[seed.AccountIndex]);
            playlist.Id = seed.Id;
            playlist.Name = seed.Name;
            foreach (var songId in seed.SongIds)
            {
                var song = library.GetSong(accs[seed.AccountIndex], songId);
                if (song is not null) playlist.Append(song);
            }
            library.SaveContext();
        }
        foreach (var seed in Radios)
        {
            var radio = library.CreateRadio(accs[seed.AccountIndex]);
            radio.Id = seed.Id;
            radio.Title = seed.Title;
            radio.Url = seed.Url;
            radio.SiteUrl = seed.SiteUrl;
        }
        library.SaveContext();
    }
}
