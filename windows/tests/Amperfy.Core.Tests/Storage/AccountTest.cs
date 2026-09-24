// Ported 1:1 from XCTest: keep the original assertion style (count comparisons, force unwraps).
#pragma warning disable xUnit2013, CS8602

using Amperfy.Core.Player;
using Amperfy.Core.Tests.Helper;
using static Amperfy.Core.Tests.Player.TestUtil;

namespace Amperfy.Core.Tests.Storage;

/// Port of AmperfyKitTests/Cases/Storage/ManagedObjects/AccountTest.swift
public class AccountTest
{
    private readonly CoreDataHelper cdHelper;
    private readonly LibraryStorage library;
    private Account? testAccount;

    public AccountTest()
    {
        cdHelper = new CoreDataHelper();
        library = cdHelper.CreateInMemoryLibrary();
    }

    [Fact]
    public void TestCreation()
    {
        Assert.Equal(0, library.GetAllAccounts().Count);
        testAccount = library.GetAccount(TestAccountInfo.Create1());
        Assert.Equal(TestAccountInfo.Test1ServerHash, testAccount.ServerHash);
        Assert.Equal(TestAccountInfo.Test1UserHash, testAccount.UserHash);
        Assert.Equal(TestAccountInfo.Test1ApiType, testAccount.ApiType);
        Assert.Equal(1, library.GetAllAccounts().Count);

        var secondAccount = library.GetAccount(TestAccountInfo.Create2());
        Assert.Equal(TestAccountInfo.Test2ServerHash, secondAccount.ServerHash);
        Assert.Equal(TestAccountInfo.Test2UserHash, secondAccount.UserHash);
        Assert.Equal(TestAccountInfo.Test2ApiType, secondAccount.ApiType);
        Assert.Equal(2, library.GetAllAccounts().Count);

        testAccount = library.GetAccount(TestAccountInfo.Create1());
        Assert.Equal(TestAccountInfo.Test1ServerHash, testAccount.ServerHash);
        Assert.Equal(TestAccountInfo.Test1UserHash, testAccount.UserHash);
        Assert.Equal(TestAccountInfo.Test1ApiType, testAccount.ApiType);
        Assert.Equal(2, library.GetAllAccounts().Count);

        secondAccount = library.GetAccount(TestAccountInfo.Create2());
        Assert.Equal(TestAccountInfo.Test2ServerHash, secondAccount.ServerHash);
        Assert.Equal(TestAccountInfo.Test2UserHash, secondAccount.UserHash);
        Assert.Equal(TestAccountInfo.Test2ApiType, secondAccount.ApiType);
        Assert.Equal(2, library.GetAllAccounts().Count);
    }

    [Fact]
    public void TestDefaultCreation()
    {
        Assert.Equal(0, library.GetAllAccounts().Count);
        var defaultAccount = library.GetAccount(new AccountInfo("", "", BackendApiType.NotDetected));
        Assert.Equal("", defaultAccount.ServerHash);
        Assert.Equal("", defaultAccount.UserHash);
        Assert.Equal(BackendApiType.NotDetected, defaultAccount.ApiType);
        Assert.Equal(1, library.GetAllAccounts().Count);

        testAccount = library.GetAccount(TestAccountInfo.Create1());
        Assert.Equal(TestAccountInfo.Test1ServerHash, testAccount.ServerHash);
        Assert.Equal(TestAccountInfo.Test1UserHash, testAccount.UserHash);
        Assert.Equal(TestAccountInfo.Test1ApiType, testAccount.ApiType);
        Assert.Equal(2, library.GetAllAccounts().Count);

        Assert.Equal("", defaultAccount.ServerHash);
        Assert.Equal("", defaultAccount.UserHash);
        Assert.Equal(BackendApiType.NotDetected, defaultAccount.ApiType);
        Assert.NotSame(testAccount, defaultAccount);

        var secondAccount = library.GetAccount(TestAccountInfo.Create2());
        Assert.Equal(TestAccountInfo.Test2ServerHash, secondAccount.ServerHash);
        Assert.Equal(TestAccountInfo.Test2UserHash, secondAccount.UserHash);
        Assert.Equal(TestAccountInfo.Test2ApiType, secondAccount.ApiType);
        Assert.Equal(3, library.GetAllAccounts().Count);
    }
}
