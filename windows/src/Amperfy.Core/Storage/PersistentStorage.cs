using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Amperfy.Core.Storage;

/// Owns the settings and the main library storage (port of PersistentStorage.swift).
public sealed class PersistentStorage : IDisposable
{
    /// Increase when the database schema changes; add a migration step in MigrateSchema.
    public const int SchemaVersion = 1;

    public AmperfySettings Settings { get; }
    public LibraryStorage Main { get; }
    public string DataDirectory { get; }

    private PersistentStorage(string dataDirectory, AmperfySettings settings, LibraryStorage main)
    {
        DataDirectory = dataDirectory;
        Settings = settings;
        Main = main;
    }

    /// Opens (or creates) the storage in the given data directory.
    public static PersistentStorage Open(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var settings = new AmperfySettings(Path.Combine(dataDirectory, "settings.json"));
        var dbPath = Path.Combine(dataDirectory, "amperfy.db");
        var context = CreateContext($"Data Source={dbPath};Cache=Shared;Pooling=False");
        return new PersistentStorage(dataDirectory, settings, new LibraryStorage(context));
    }

    /// In-memory storage for tests. The connection stays open for the lifetime of the context.
    public static PersistentStorage CreateInMemory(AmperfySettings? settings = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var context = new AmperfyDbContext(connection.ConnectionString, connection);
        context.Database.EnsureCreated();
        return new PersistentStorage(Path.GetTempPath(), settings ?? new AmperfySettings(), new LibraryStorage(context));
    }

    private static AmperfyDbContext CreateContext(string connectionString)
    {
        var context = new AmperfyDbContext(connectionString);
        var created = context.Database.EnsureCreated();
        var connection = context.Database.GetDbConnection();
        context.Database.OpenConnection();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;";
            cmd.ExecuteNonQuery();
        }
        var version = GetUserVersion(connection);
        if (created || version == 0)
        {
            SetUserVersion(connection, SchemaVersion);
        }
        else if (version < SchemaVersion)
        {
            MigrateSchema(connection, version);
            SetUserVersion(connection, SchemaVersion);
        }
        return context;
    }

    private static int GetUserVersion(System.Data.Common.DbConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void SetUserVersion(System.Data.Common.DbConnection connection, int version)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA user_version = {version};";
        cmd.ExecuteNonQuery();
    }

    private static void MigrateSchema(System.Data.Common.DbConnection connection, int fromVersion)
    {
        // Future schema migrations go here (ALTER TABLE ...), one step per version.
        AmperfyLog.Info("PersistentStorage", $"Migrating database schema from v{fromVersion} to v{SchemaVersion}");
    }

    public void Dispose()
    {
        Main.SaveContext();
        Main.Context.Dispose();
        Settings.Dispose();
    }
}
