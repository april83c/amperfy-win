using Amperfy.Core.Tests.Helper;
using Amperfy.Core.Api.Subsonic;

namespace Amperfy.Core.Tests.Api.Subsonic;

public class SubsonicVersionTest
{
    [Fact]
    public void TestCreationGood()
    {
        var v1 = new SubsonicVersion(0, 0, 0);
        Assert.Equal(0, v1.Major);
        Assert.Equal(0, v1.Minor);
        Assert.Equal(0, v1.Patch);

        var v2 = new SubsonicVersion(150, 30, 3);
        Assert.Equal(150, v2.Major);
        Assert.Equal(30, v2.Minor);
        Assert.Equal(3, v2.Patch);
    }

    [Fact]
    public void TestCreationGoodString()
    {
        var v1 = SubsonicVersion.Create("0.0.0");
        Assert.NotNull(v1);
        Assert.Equal(0, v1.Major);
        Assert.Equal(0, v1.Minor);
        Assert.Equal(0, v1.Patch);

        var v2 = SubsonicVersion.Create("150.30.3");
        Assert.NotNull(v2);
        Assert.Equal(150, v2.Major);
        Assert.Equal(30, v2.Minor);
        Assert.Equal(3, v2.Patch);
    }

    [Fact]
    public void TestCreationBadString1()
    {
        Assert.Null(SubsonicVersion.Create("asdf"));
        Assert.Null(SubsonicVersion.Create("0.0.-1"));
        Assert.Null(SubsonicVersion.Create("0.-1.0"));
        Assert.Null(SubsonicVersion.Create("-1.0.0"));
        Assert.Null(SubsonicVersion.Create("0.0.-121"));
        Assert.Null(SubsonicVersion.Create("aa.0.0"));
        Assert.Null(SubsonicVersion.Create("0.bf.5"));
        Assert.Null(SubsonicVersion.Create("0.0.-"));
        Assert.Null(SubsonicVersion.Create("a.a.123"));
    }

    [Fact]
    public void TestCreationBadString2()
    {
        Assert.Null(SubsonicVersion.Create("0.0"));
        Assert.Null(SubsonicVersion.Create("1.14"));
        Assert.Null(SubsonicVersion.Create("0.0.0.0"));
        Assert.Null(SubsonicVersion.Create("0.0.0.5"));
        Assert.Null(SubsonicVersion.Create("1.2.35.99"));
        Assert.Null(SubsonicVersion.Create("0.0.0.0.0.0.0.0"));
        Assert.Null(SubsonicVersion.Create("0.0.5.asdf"));
        Assert.Null(SubsonicVersion.Create("0"));
    }

    [Fact]
    public void TestToStringFunctionality()
    {
        Assert.Equal("0.0.0", new SubsonicVersion(0, 0, 0).Description);
        Assert.Equal("150.30.3", new SubsonicVersion(150, 30, 3).Description);
    }

    [Fact]
    public void TestCompareEqual()
    {
        var v1 = new SubsonicVersion(0, 0, 0);
        var v2 = new SubsonicVersion(0, 0, 0);
        Assert.True(v1 == v2);

        var v3 = new SubsonicVersion(0, 0, 1);
        var v4 = new SubsonicVersion(30, 5, 1);
        var v5 = new SubsonicVersion(30, 5, 1);

        Assert.False(v1 == v3);
        Assert.False(v1 == v4);
        Assert.False(v3 == v4);
        Assert.True(v4 == v5);
    }

    [Fact]
    public void TestCompareGreater()
    {
        var v1 = new SubsonicVersion(0, 0, 0);
        var v2 = new SubsonicVersion(5, 0, 0);
        var v3 = new SubsonicVersion(0, 0, 1);
        var v4 = new SubsonicVersion(30, 5, 1);
        var v5 = new SubsonicVersion(0, 0, 0);

        Assert.False(v1 > v2);
        Assert.True(v4 > v3);
        Assert.False(v1 > v4);
        Assert.True(v3 > v1);
        Assert.False(v1 > v5);
    }

    [Fact]
    public void TestCompareLower()
    {
        var v1 = new SubsonicVersion(0, 0, 0);
        var v2 = new SubsonicVersion(5, 0, 0);
        var v3 = new SubsonicVersion(0, 0, 1);
        var v4 = new SubsonicVersion(30, 5, 1);
        var v5 = new SubsonicVersion(0, 0, 0);

        Assert.True(v1 < v2);
        Assert.False(v4 < v3);
        Assert.True(v1 < v4);
        Assert.False(v3 < v1);
        Assert.False(v1 < v5);
    }

    [Fact]
    public void TestCompareGreaterEqual()
    {
        var v1 = new SubsonicVersion(0, 0, 0);
        var v2 = new SubsonicVersion(5, 0, 0);
        var v3 = new SubsonicVersion(0, 0, 1);
        var v4 = new SubsonicVersion(30, 5, 1);
        var v5 = new SubsonicVersion(0, 0, 0);

        Assert.False(v1 >= v2);
        Assert.True(v4 >= v3);
        Assert.False(v1 >= v4);
        Assert.True(v3 >= v1);
        Assert.True(v1 >= v5);
    }

    [Fact]
    public void TestCompareLowerEqual()
    {
        var v1 = new SubsonicVersion(0, 0, 0);
        var v2 = new SubsonicVersion(5, 0, 0);
        var v3 = new SubsonicVersion(0, 0, 1);
        var v4 = new SubsonicVersion(30, 5, 1);
        var v5 = new SubsonicVersion(0, 0, 0);

        Assert.True(v1 <= v2);
        Assert.False(v4 <= v3);
        Assert.True(v1 <= v4);
        Assert.False(v3 <= v1);
        Assert.True(v1 <= v5);
    }

    [Fact]
    public void TestCompareIsLexicographic()
    {
        var v1_1_1 = new SubsonicVersion(1, 1, 1);
        var v1_9_0 = new SubsonicVersion(1, 9, 0);
        var v1_14_0 = new SubsonicVersion(1, 14, 0);
        var v2_0_0 = new SubsonicVersion(2, 0, 0);

        Assert.False(v1_1_1 > v1_9_0);
        Assert.True(v1_1_1 < v1_9_0);
        Assert.False(v1_1_1 >= v1_9_0);
        Assert.True(v1_14_0 > v1_9_0);
        Assert.False(v1_14_0 > v2_0_0);
        Assert.True(v1_14_0 <= v2_0_0);
        Assert.True(new SubsonicVersion(1, 11, 0) < SubsonicVersion.AuthenticationTokenRequiredServerApi);
        Assert.False(new SubsonicVersion(1, 13, 0) < SubsonicVersion.AuthenticationTokenRequiredServerApi);
        Assert.False(new SubsonicVersion(1, 16, 1) < SubsonicVersion.AuthenticationTokenRequiredServerApi);
    }
}
