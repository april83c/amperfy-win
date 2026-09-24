using Amperfy.Core.Tests.Helper;
using Amperfy.Core.Api.Subsonic;

namespace Amperfy.Core.Tests.Api.Subsonic;

public class SsPingParserTest
{
    private readonly byte[] _xmlData = TestFiles.GetTestFileData("Subsonic", "ping_example_1");

    [Fact]
    public void TestParsing()
    {
        var parserDelegate = new SsPingParserDelegate();
        parserDelegate.Parse(_xmlData);

        Assert.Null(parserDelegate.Error);
        Assert.True(parserDelegate.IsAuthValid);
        Assert.Equal("1.1.1", parserDelegate.ServerApiVersion);
    }
}
