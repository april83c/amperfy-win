namespace Amperfy.Core.Tests.Helper;

public static class TestAccountInfo
{
    public const string Test1ServerHash = "111Server";
    public const string Test1UserHash = "111User";
    public const BackendApiType Test1ApiType = BackendApiType.Ampache;
    public const string Test2ServerHash = "22-S";
    public const string Test2UserHash = "22-U";
    public const BackendApiType Test2ApiType = BackendApiType.Subsonic;

    public static AccountInfo Create1() => new(Test1ServerHash, Test1UserHash, Test1ApiType);
    public static AccountInfo Create2() => new(Test2ServerHash, Test2UserHash, Test2ApiType);
}

public static class TestFiles
{
    public static readonly Uri TestUrl = new("https://github.com/BLeeEZ/amperfy");

    /// Reads a sample file from Samples/{area}/{name}.{ext} (copied to the test output directory).
    public static byte[] GetTestFileData(string area, string name, string withExtension = "xml")
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Samples", area, $"{name}.{withExtension}");
        return File.ReadAllBytes(path);
    }
}

/// Port of CoreDataHelper: in-memory storage factory.
public sealed class CoreDataHelper
{
    public CoreDataSeeder Seeder { get; } = new();

    public LibraryStorage CreateInMemoryLibrary()
    {
        CacheFileManager.Shared = new CacheFileManager(Path.Combine(Path.GetTempPath(), "amperfy-tests", Guid.NewGuid().ToString("N")));
        return PersistentStorage.CreateInMemory().Main;
    }

    public LibraryStorage CreateSeededStorage()
    {
        var library = CreateInMemoryLibrary();
        Seeder.Seed(library);
        return library;
    }
}
