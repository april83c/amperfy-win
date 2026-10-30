using Amperfy.Core.Tests.Helper;

namespace Amperfy.Core.Tests.Storage;

/// Moving and removing several playlist items at once (multi selection in the playlist view).
public class PlaylistMultiEditTest
{
    private readonly LibraryStorage _library;
    private readonly Playlist _playlist;
    private readonly List<Song> _songs;

    public PlaylistMultiEditTest()
    {
        var helper = new CoreDataHelper();
        _library = helper.CreateSeededStorage();
        var account = _library.GetAccount(TestAccountInfo.Create1());
        _songs = helper.Seeder.Songs.Take(8).Select(s => _library.GetSong(account, s.Id)!).ToList();
        _playlist = _library.CreatePlaylist(account);
        _playlist.Append(_songs.Cast<AbstractPlayable>().ToList()); // A B C D E F G H
        _library.SaveContext();
    }

    private string Order() => string.Concat(_playlist.Items.Select(i => (char)('A' + _songs.IndexOf((Song)i.Playable!))));

    private PlaylistItem Item(char c) => _playlist.Items.First(i => ReferenceEquals(i.Playable, _songs[c - 'A']));

    private List<PlaylistItem> Items(string letters) => letters.Select(Item).ToList();

    [Theory]
    [InlineData("BD", 'F', "ACEFBDGH")]
    [InlineData("FB", 'H', "ACDEGHBF")] // keeps playlist order, not selection order
    [InlineData("GH", null, "GHABCDEF")]
    [InlineData("AB", 'C', "CABDEFGH")]
    [InlineData("CE", 'D', "ABDCEFGH")] // anchor inside the block -> after the preceding non-moved item... D is not moved
    [InlineData("CD", 'D', "ABCDEFGH")] // anchor is a moved item, nothing changes
    public void MoveItems_KeepsRelativeOrder(string move, char? after, string expected)
    {
        _playlist.MoveItems(Items(move), after is { } a ? Item(a) : null);
        _library.SaveContext();
        Assert.Equal(expected, Order());
        Assert.Equal(_playlist.Items.Count, _playlist.Items.Select(i => i.Order).Distinct().Count());
        // the stored order survives reloading the cache
        _playlist.InvalidateItemCache();
        Assert.Equal(expected, Order());
    }

    [Fact]
    public void MoveItems_NoChange_ReturnsFalse()
    {
        Assert.False(_playlist.MoveItems(Items("BC"), Item('A')));
        Assert.True(_playlist.MoveItems(Items("BC"), Item('D')));
    }

    [Fact]
    public void MoveItems_ManyTimes_RenumbersWhenOrdersRunOut()
    {
        for (var i = 0; i < 50; i++) _playlist.MoveItems(Items("H"), Item('A'));
        Assert.Equal("AHBCDEFG", Order());
        for (var i = 0; i < 50; i++)
        {
            _playlist.MoveItems(Items("BC"), Item('A'));
            _playlist.MoveItems(Items("H"), Item('A'));
        }
        _playlist.InvalidateItemCache();
        Assert.Equal("AHBCDEFG", Order());
    }

    [Fact]
    public void RemoveItems_ReturnsOriginalIndexesAndUpdatesCounts()
    {
        var indexes = _playlist.RemoveItems(Items("FBD"));
        _library.SaveContext();
        Assert.Equal([1, 3, 5], indexes);
        Assert.Equal("ACEGH", Order());
        Assert.Equal(5, _playlist.SongCount);
        Assert.Equal(5, _playlist.Items.Count);
    }
}
