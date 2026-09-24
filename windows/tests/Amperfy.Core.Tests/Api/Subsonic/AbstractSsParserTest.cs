using Amperfy.Core.Api.Subsonic;
using Amperfy.Core.Tests.Helper;

namespace Amperfy.Core.Tests.Api.Subsonic;

/// Port of AbstractSsTest.swift. xUnit runs the [Fact]s of this abstract base for every derived
/// test class (the base itself is never instantiated).
public abstract class AbstractSsParserTest
{
    protected CoreDataHelper CdHelper { get; }
    protected LibraryStorage Library { get; }
    protected Account Account { get; }
    protected byte[]? XmlData { get; set; }
    protected byte[] XmlErrorData { get; }
    protected SsIDsParserDelegate SsIdParserDelegate { get; set; }
    protected SsXmlParser? SsParserDelegate { get; set; }

    protected AbstractSsParserTest()
    {
        CdHelper = new CoreDataHelper();
        Library = CdHelper.CreateInMemoryLibrary();
        Account = Library.GetAccount(TestAccountInfo.Create1());
        _ = Library.GetAccount(TestAccountInfo.Create2());
        XmlErrorData = GetTestFileData("error_example_1");
        SsIdParserDelegate = new SsIDsParserDelegate();
    }

    protected static byte[] GetTestFileData(string name) => TestFiles.GetTestFileData("Subsonic", name);

    protected PrefetchIdTester PrefetchIdTester => new(Library, SsIdParserDelegate.PrefetchIDs);

    [Fact]
    public void TestErrorParsing()
    {
        CreateParserDelegate();
        var parserDelegate = SsParserDelegate;
        Assert.NotNull(parserDelegate);
        parserDelegate.Parse(XmlErrorData);

        var error = parserDelegate.Error;
        Assert.NotNull(error);
        Assert.Equal(40, error.StatusCode);
        Assert.Equal("Wrong username or password", error.Message);
    }

    [Fact]
    public void TestParsing() => ReTestParsing();

    [Fact]
    public void TestParsingTwice()
    {
        ReTestParsing();
        SsIdParserDelegate = new SsIDsParserDelegate();
        AdjustmentsForSecondParsingDelegate();
        ReTestParsing();
    }

    protected void ReTestParsing()
    {
        var data = XmlData;
        Assert.NotNull(data);
        SsIdParserDelegate.Parse(data); // like Swift: the parse result is not checked (samples may be malformed)
        Assert.Null(SsIdParserDelegate.Error);

        CreateParserDelegate();
        var parserDelegate = SsParserDelegate;
        Assert.NotNull(parserDelegate);
        parserDelegate.Parse(data);
        Assert.Null(parserDelegate.Error);
        // CoreData fetches include pending changes, EF Core queries only see saved data
        Library.SaveContext();
        Assert.False(Library.HasChanges); // save must not have failed
        CheckCorrectParsing();
    }

    /// Creates the parser under test (SsParserDelegate).
    protected abstract void CreateParserDelegate();

    // Override in concrete test class if needed
    protected virtual void AdjustmentsForSecondParsingDelegate() { }

    protected abstract void CheckCorrectParsing();

    protected static void AssertIsAccount1(Account? account)
    {
        Assert.Equal(TestAccountInfo.Test1ServerHash, account?.ServerHash);
        Assert.Equal(TestAccountInfo.Test1UserHash, account?.UserHash);
    }

    protected static int IntId(string id) => int.Parse(id);
}
