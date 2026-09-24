using Amperfy.Core.Tests.Helper;
using Amperfy.Core.Api.Subsonic;

namespace Amperfy.Core.Tests.Api.Subsonic;

public class SsXmlParserTest
{
    private readonly byte[] _xmlData = TestFiles.GetTestFileData("Subsonic", "error_example_1");

    [Fact]
    public void TestParsing()
    {
        var parserDelegate = new SsPingParserDelegate();
        parserDelegate.Parse(_xmlData);

        var error = parserDelegate.Error;
        Assert.NotNull(error);
        Assert.Equal(40, error.StatusCode);
        Assert.Equal("Wrong username or password", error.Message);
        Assert.Equal(SubsonicError.WrongUsernameOrPassword, error.SubsonicError);
        Assert.False(parserDelegate.IsAuthValid);
    }
}
