# Amperfy for Windows — Architecture & Porting Conventions

This is a C# port of the Swift Amperfy app (`../AmperfyKit` = core, `../Amperfy` = UI).
The Swift sources in the repository root are the reference implementation.

## Projects

| Project | Target | Purpose |
|---|---|---|
| `src/Amperfy.Core` | `net10.0` (cross platform) | Port of **AmperfyKit**: model, storage, server APIs, sync, downloads, player logic |
| `src/Amperfy.App` | `net10.0-windows10.0.26100.0`, WinUI 3 (Windows App SDK 2.x), unpackaged | UI + Windows specific services (audio output, SMTC, notifications) |
| `tests/Amperfy.Core.Tests` | `net10.0`, xUnit | Port of **AmperfyKitTests** (+ new tests). Runs on Linux and Windows |

Build/test locally (Linux): `export PATH=$PATH:/root/.dotnet && dotnet test tests/Amperfy.Core.Tests`.
The WinUI app only builds on Windows (GitHub Actions `windows.yml`).

## Threading model ("MainActor")

Swift uses `@MainActor` + CoreData main context. The C# port mirrors that:

* **All storage (EF Core) and player state is owned by the main thread** (UI thread in the app,
  a `SingleThreadSynchronizationContext` in tests). Never touch entities or `LibraryStorage` from
  a thread-pool thread.
* Network I/O and file I/O are awaited (`await httpClient...`) — continuations return to the main
  thread automatically because the synchronization context is captured. **Do not use
  `ConfigureAwait(false)` in code that touches storage afterwards.**
* CPU heavy work that doesn't touch entities (e.g. first "IDs" parser pass) may run in `Task.Run`.
* Timers: `MainThread.CreateTimer(interval, tick)`; posting from background: `MainThread.Post(...)`.
* Swift `storage.async.perform { ... }` blocks become plain code on the main thread using the
  same `LibraryStorage` (there is only one context). Call `library.SaveContext()` where Swift saved.

## Storage (CoreData → EF Core + SQLite)

* `AmperfyDbContext` (EF Core, SQLite, **lazy loading proxies**). CoreData model v49 → entities
  in `Model/`. Library entities use TPH in table `LibraryEntities` (`Kind` discriminator).
* Swift had *EntityWrappers* (`Song` wrapping `SongMO`); in C# the entity class **is** the wrapper
  (`Model/Song.cs` contains the logic of both `SongMO` and `Song`).
* **Always create entities through `LibraryStorage.CreateXxx(account)`** (they must be proxies for
  lazy loading). Leaf entities without collections (`PlaylistItem`, `ScrobbleEntry`, `LogEntry`,
  `Download`, `EmbeddedArtwork`, `SearchHistoryItem`) may be `new`-ed but must be added to the
  context (the LibraryStorage helpers do that).
* Primary key is `Pk` (int). The server id is `Id` (string), like in Swift.
* Raw columns vs. logic properties: e.g. `Album.NameRaw` is the column, `Album.Name` is the Swift
  property (with "Unknown Album" fallback and section-initial update). Same pattern for
  `SongsRaw` (collection) vs `Songs` (sorted list), `SongCountRaw` vs `SongCount`, etc.
* Denormalized counts (`songCount`, `albumCount`, ...) are recalculated on `SaveChanges`
  (CoreData did it in `willSave`). `Playlist.SongCountRaw` is updated immediately by Playlist ops.
* `Playlist` keeps items ordered by `PlaylistItem.Order` (Swift `orderDistance` algorithm).
* Queries: `LibraryStorage` (`Storage/LibraryStorage*.cs`, `partial`). Add new queries in a new
  partial file of your area (e.g. `Storage/LibraryStorage.Downloads.cs`) to avoid merge conflicts.
* **Queries only see saved data** (unlike CoreData fetches, which include unsaved changes). Call
  `library.SaveContext()` before querying for entities you just created/changed. Navigation
  properties and the prefetch dictionaries do see unsaved objects.
* Prefetch (Swift `PrefetchIdContainer` / `getElements`): `LibraryStorage.GetElements(account, ids)`.
* Swift `NSManagedObjectID` references between contexts are not needed (single context).
* Container identifiers: `PlayableContainerIdentifier(type, Pk.ToString())`.

## Naming

* C# conventions: PascalCase, `Async` suffix for async methods, `I` prefix for interfaces.
* Swift `func sync(album:)` → `SyncAsync(Album album)`; `requestX` → `RequestXAsync`.
* Swift `throws` → exceptions. Swift errors: `ResponseError`, `BackendError`, `AuthenticationError`
  (`Api/ApiContracts.cs`), `DownloadException` (`Downloads/DownloadContracts.cs`).
* Swift `os_log` → `AmperfyLog.Info/Warning/Error(category, message)`.
* `EventLogger.Report(topic, exception, displayPopup)` for user visible errors.
* Swift `NotificationCenter` → `EventNotificationHandler.Post(AmperfyNotification.X, sender, payload)`.
* Swift `Date()` → `DateTime.UtcNow` (store UTC).
* Swift `Int16`/`Int32` limits don't apply; use `int`/`long`.

## Namespaces

`Amperfy.Core.Model` (entities), `Amperfy.Core.Storage` (context, LibraryStorage, settings, cache
files), `Amperfy.Core.Api` (+ `.Subsonic`, `.Ampache`), `Amperfy.Core.Downloads`,
`Amperfy.Core.Player`, `Amperfy.Core.Common`. Global usings: see `GlobalUsings.cs`.

## Tests

* Port Swift XCTest cases to xUnit 1:1 where possible (same names, same assertions).
* XML samples live in `tests/Amperfy.Core.Tests/Samples/{Subsonic,Ampache}/` (copied to output).
* `Helper/TestStorage` creates an in-memory database + test account.
* Async tests that touch storage: `SingleThreadSynchronizationContext.Run(async () => { ... })`.

## Audio output (Windows app)

`Services/Audio` implements the core's `IAudioStreamingPlayer` and `ISystemMediaControls`; `AudioBackend` creates both.

| Feature | Support |
|---|---|
| Engine | `WindowsAudioEngine` picks an engine for each `Play`. `MediaPlayerAudioEngine` (Windows.Media.Playback) is the default. `AudioGraphAudioEngine` plays entries started while the EQ is active; `Queue` stays on the active engine. |
| Gapless | MediaPlayer: `MediaPlaybackList`; `Play` replaces the list and `Queue` appends to it. AudioGraph: the next input node is prepared and started from the audio thread. Transitions raise `DidFinishPlaying(old)` and `DidStartPlaying(new)`, keyed by the URL exactly as passed. |
| Custom HTTP headers | Headers can't be passed to Media Foundation. With headers, the stream is read through `HttpRangeStream` (range requests), wrapped as an `IRandomAccessStream`. This needs a known length; otherwise the plain URI is played without the headers and a warning is logged. |
| Cached files | `file://` URLs open through `StorageFile`. A file with an unknown extension opens as a stream with the MIME type. |
| Radio / ICY | Live streams (the playing or next queue item is a `Radio` with that URL) are always played by MediaPlayer. `IcyMetadataPoller` (core) reads `StreamTitle` about every 15 s over a separate connection, reading one metadata block per poll, and raises `DidReadMetadata`. SHOUTcast v1 "ICY 200 OK" responses are rejected by HttpClient, so those stations show no title. |
| Replay gain | MediaPlayer: `Volume = volume * min(replayGain, 1)`, so boosts (gain > 1) are clipped. AudioGraph: the input node gain, which can be > 1. |
| EQ | The 10-band EQ needs the AudioGraph path: 3 built-in `EqualizerEffectDefinition`s with 4 bands each (10 used, 1 octave, ±18 dB range) on a submix node. Unpackaged apps can't register custom `IBasicAudioEffect`s, so there is no EQ on the MediaPlayer path. Turning the EQ on applies from the next started track; changing the gains applies immediately. |
| AudioGraph limits | It is bound to the default output device at creation: a device change raises an error and the player restarts the engine. The rate uses `PlaybackSpeedFactor`, which may change the pitch. If the graph or an input node can't be created, playback falls back to MediaPlayer (`PlaybackUnsupported`). |
| System media controls | The SMTC of a dedicated, never-playing `MediaPlayer` with its `CommandManager` disabled (manual control). It provides metadata, a thumbnail (artwork file, or the app icon), the timeline, shuffle, repeat and rate. Buttons and requests are posted to the main thread. The playback engines disable their own SMTC integration. |

