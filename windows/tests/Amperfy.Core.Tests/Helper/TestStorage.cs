namespace Amperfy.Core.Tests.Helper;

/// Creates an in-memory storage with a test account.
public sealed class TestStorage : IDisposable
{
    public PersistentStorage Storage { get; }
    public LibraryStorage Library => Storage.Main;
    public Account Account { get; }
    public string CacheDir { get; }

    public TestStorage()
    {
        CacheDir = Path.Combine(Path.GetTempPath(), "amperfy-tests", Guid.NewGuid().ToString("N"));
        CacheFileManager.Shared = new CacheFileManager(CacheDir);
        Storage = PersistentStorage.CreateInMemory();
        Account = Library.GetAccount(AccountInfo.Create("https://test.example", "testuser", BackendApiType.Subsonic));
    }

    public void Dispose()
    {
        Storage.Main.Context.Dispose();
        try { Directory.Delete(CacheDir, true); } catch { }
    }
}
