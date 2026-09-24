namespace Amperfy.Core.Sync;

/// Swift: LibraryUpdaterCallbacks
public interface ILibraryUpdaterCallbacks
{
    void StartOperation(string name, int totalCount);
    void TickOperation();
}

/// Library version handling at app start (port of Storage/LibraryUpdater.swift).
///
/// The Windows app starts with a fresh database (library sync version <see cref="SettingEnumerationExtensions.NewestLibrarySyncVersion"/>),
/// so the iOS CoreData/cache migrations (alphabetic section initials, denormalized counts, playlist
/// item order, account directories, ...) are not needed: outdated versions are only bumped.
/// The obsolete account clean up is ported completely.
public sealed class LibraryUpdater
{
    private const string LogCategory = "LibraryUpdater";

    private readonly LibraryStorage _library;
    private readonly AmperfySettings _settings;

    public LibraryUpdater(LibraryStorage library, AmperfySettings settings)
    {
        _library = library;
        _settings = settings;
    }

    public bool IsVisualUpdateNeeded => _settings.App.LibrarySyncVersion != SettingEnumerationExtensions.NewestLibrarySyncVersion;

    /// Deletes database accounts (and their library entries) which have no settings/credentials anymore.
    /// Perform only at the beginning of the app start - later these accounts could already be used.
    public void PerformAccountCleanUpIfNecessary()
    {
        var settingAccounts = _settings.Accounts.AllAccounts.ToHashSet();
        var obsoleteAccountInfos = _library.GetAllAccounts().Select(a => a.Info).Where(info => !settingAccounts.Contains(info)).ToList();
        foreach (var obsoleteAccountInfo in obsoleteAccountInfos)
        {
            var ident = obsoleteAccountInfo.Ident;
            AmperfyLog.Info(LogCategory, $"Delete obsolete account (START): {ident}");
            // re-fetch: cleaning the storage clears the change tracker
            _library.CleanStorageOfObsoleteAccountEntries(_library.GetAccount(obsoleteAccountInfo));
            var obsoleteAccount = _library.GetAccount(obsoleteAccountInfo);
            _library.DeleteAccount(obsoleteAccount);
            _library.SaveContext();
            AmperfyLog.Info(LogCategory, $"Delete obsolete account (DONE): {ident}");
        }
    }

    /// Small blocking updates before the UI is shown (Swift: performSmallBlockingLibraryUpdatesIfNeeded).
    public void PerformSmallBlockingLibraryUpdatesIfNeeded()
    {
        var version = _settings.App.LibrarySyncVersion;
        if (version < LibrarySyncVersion.V20)
        {
            // Swift: alphabetic section initials (v12), durations (v13/v15), playlist artwork items (v16) and the
            // split of the streaming format preference (v20). A Windows database never contains such data.
            AmperfyLog.Info(LogCategory, $"Perform blocking library update: {version} -> {LibrarySyncVersion.V20} (nothing to migrate)");
            _settings.App.LibrarySyncVersion = LibrarySyncVersion.V20;
        }
    }

    public void CancelLibraryUpdate() => AmperfyLog.Info(LogCategory, "LibraryUpdate: cancel");

    /// Longer updates with progress (Swift: performLibraryUpdateWithStatus).
    public Task PerformLibraryUpdateWithStatusAsync(ILibraryUpdaterCallbacks notifier)
    {
        var version = _settings.App.LibrarySyncVersion;
        if (version < SettingEnumerationExtensions.NewestLibrarySyncVersion)
        {
            AmperfyLog.Info(LogCategory, $"Perform library update: {version} -> {SettingEnumerationExtensions.NewestLibrarySyncVersion} (nothing to migrate)");
            notifier.StartOperation("Library Update", 1);
            _settings.App.LibrarySyncVersion = SettingEnumerationExtensions.NewestLibrarySyncVersion;
            notifier.TickOperation();
        }
        return Task.CompletedTask;
    }
}
