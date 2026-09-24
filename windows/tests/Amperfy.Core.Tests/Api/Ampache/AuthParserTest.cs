using System.Globalization;
using Amperfy.Core.Api.Ampache;
using Amperfy.Core.Tests.Helper;

namespace Amperfy.Core.Tests.Api.Ampache;

/// Port of AuthParserTest.swift
public class AuthParserTest
{
    private readonly byte[] _xmlData = TestFiles.GetTestFileData("Ampache", "handshake");

    [Fact]
    public void TestParsing()
    {
        var parserDelegate = new AuthParserDelegate();
        parserDelegate.Parse(_xmlData);
        Assert.Null(parserDelegate.Error);
        Assert.Equal("5.0.0", parserDelegate.ServerApiVersion);
        var handshake = parserDelegate.AuthHandshake;
        Assert.NotNull(handshake);
        Assert.Equal("cfj3f237d563f479f5223k23189dbb34", handshake!.Token);
        Assert.Equal("2021-03-31T18:16:10+10:00".AsIso8601Date(), handshake.SessionExpire);
        Assert.Equal("2021-03-31T13:32:27+10:00".AsIso8601Date(), handshake.LibraryChangeDates.DateOfLastAdd);
        Assert.Equal("2021-03-31T17:15:18+10:00".AsIso8601Date(), handshake.LibraryChangeDates.DateOfLastClean);
        Assert.Equal("2021-03-31T17:15:25+10:00".AsIso8601Date(), handshake.LibraryChangeDates.DateOfLastUpdate);
        Assert.Equal(55, handshake.SongCount);
        Assert.Equal(16, handshake.ArtistCount);
        Assert.Equal(8, handshake.AlbumCount);
        Assert.Equal(6, handshake.GenreCount);
        Assert.Equal(19, handshake.PlaylistCount);
        Assert.Equal(3, handshake.PodcastCount);
        Assert.Equal(2, handshake.VideoCount);
    }
}
