using System.Globalization;
using Amperfy.Core.Api.Ampache;
using Amperfy.Core.Tests.Helper;

namespace Amperfy.Core.Tests.Api.Ampache;

/// Port of ErrorParserTest.swift
public class ErrorParserTest
{
    private readonly byte[] _xmlData = TestFiles.GetTestFileData("Ampache", "error-4700");

    [Fact]
    public void TestParsing()
    {
        var parserDelegate = new AmpacheXmlParser();
        parserDelegate.Parse(_xmlData);
        var error = parserDelegate.Error;
        Assert.NotNull(error);
        Assert.Equal(4700, error!.StatusCode);
        Assert.Equal("Access Denied", error.ErrorMessage);
    }
}
