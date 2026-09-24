using Amperfy.Core.Api.Ampache;
using Amperfy.Core.Tests.Helper;

namespace Amperfy.Core.Tests.Api.Ampache;

/// Port of AbstractAmpacheTest.swift: the [Fact]s declared here run for every concrete subclass.
public abstract class AbstractAmpacheTest
{
    protected CoreDataHelper CdHelper { get; }
    protected LibraryStorage Library { get; }
    protected Account Account { get; }
    protected byte[]? XmlData { get; set; }
    protected byte[] XmlErrorData { get; }
    protected IDsParserDelegate IdParserDelegate { get; set; }
    protected AmpacheXmlParser? ParserDelegate { get; set; }

    protected AbstractAmpacheTest()
    {
        CdHelper = new CoreDataHelper();
        Library = CdHelper.CreateInMemoryLibrary();
        Account = Library.GetAccount(TestAccountInfo.Create1());
        _ = Library.GetAccount(TestAccountInfo.Create2());
        XmlErrorData = TestFiles.GetTestFileData("Ampache", "error-4700");
        IdParserDelegate = new IDsParserDelegate();
    }

    protected AmpachePrefetchIdTester PrefetchIdTester => new(Library, IdParserDelegate.PrefetchIDs);

    protected static byte[] GetTestFileData(string name) => TestFiles.GetTestFileData("Ampache", name);

    [Fact]
    public void TestErrorParsing()
    {
        CreateParserDelegate();
        if (ParserDelegate is not { } parserDelegate) return;
        parserDelegate.Parse(XmlErrorData);
        Assert.NotNull(parserDelegate.Error);
        Assert.Equal(4700, parserDelegate.Error!.StatusCode);
        Assert.Equal("Access Denied", parserDelegate.Error.ErrorMessage);
    }

    [Fact]
    public void TestParsing() => ReTestParsing();

    [Fact]
    public void TestParsingTwice()
    {
        ReTestParsing();
        IdParserDelegate = new IDsParserDelegate();
        AdjustmentsForSecondParsingDelegate();
        ReTestParsing();
    }

    protected void ReTestParsing()
    {
        Assert.NotNull(XmlData);
        var data = XmlData!;
        IdParserDelegate.Parse(data);
        Assert.Null(IdParserDelegate.Error);
        CreateParserDelegate();
        if (ParserDelegate is not { } parserDelegate) return;
        parserDelegate.Parse(data);
        Assert.Null(parserDelegate.Error);
        // CoreData fetch requests include unsaved changes, EF Core queries only see saved data
        Library.SaveContext();
        CheckCorrectParsing();
    }

    protected abstract void CreateParserDelegate();

    protected virtual void AdjustmentsForSecondParsingDelegate() { }

    protected abstract void CheckCorrectParsing();

    protected static void AssertTestAccount1(Account? account)
    {
        Assert.Equal(TestAccountInfo.Test1ServerHash, account?.ServerHash);
        Assert.Equal(TestAccountInfo.Test1UserHash, account?.UserHash);
    }
}
