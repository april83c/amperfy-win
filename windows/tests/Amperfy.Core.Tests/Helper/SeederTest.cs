namespace Amperfy.Core.Tests.Helper;

public class SeederTest
{
    [Fact]
    public void SeedCreatesExpectedData()
    {
        var library = new CoreDataHelper().CreateSeededStorage();
        var acc1 = library.GetAccount(TestAccountInfo.Create1());
        Assert.Equal(2, library.GetAllAccounts().Count);
        Assert.Equal(16, library.GetSongs(acc1).Count);
        Assert.Equal(4, library.GetPlaylists(acc1).Count);
        var p = library.GetPlaylist(acc1, "9")!;
        Assert.Equal(9, p.SongCount);
        Assert.Equal(["3", "5", "10T", "36", "19", "10T", "38", "5", "41"], p.Playables.Select(x => x.Id));
        Assert.True(library.GetSong(acc1, "36")!.IsCached);
    }
}
