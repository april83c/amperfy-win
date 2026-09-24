# Amperfy for Windows

A native Windows port of [Amperfy](https://github.com/BLeeEZ/amperfy), the Subsonic/Navidrome/Ampache music player, in
C# / .NET 10 with WinUI 3 (Windows App SDK). The Swift app in the repository root is the reference
implementation. `docs/PORTING.md` explains why the port uses C# rather than Swift on Windows.

## Features
- **Servers:**
  - Subsonic (token and legacy password login), Navidrome, OpenSubsonic extensions and Ampache.
  - The API is detected automatically at login.
  - Custom HTTP headers and alternative server URLs.
  - Multiple accounts with account switching.
- **Library:**
  - Initial sync, plus background sync of album songs and the newest albums and podcasts.
  - Pages for artists, albums, songs, genres, directories/indexes, playlists, podcasts, radios, favorites, newest and recently played albums, and downloads.
  - Each page has sorting, filters, a "Jump to" letter menu, in-page search and grid/list views.
- **Home:** configurable sections (random, newest and recently played albums; random artists and songs; playlists; podcast episodes; radios).
- **Search:** local and server search with categories and search history.
- **Context menus:** Play, Shuffle, Instant Mix, queue insert/append, show album/artist, lyrics, favorite, rating, add to playlist, download, delete cache, delete on server, share, save a copy, go to site, copy ID. The same menu is used in lists, the queue and the player.
- **Playlists:** create, rename, reorder by drag & drop, remove items, add songs; synced to the server.
- **Player:**
  - Gapless playback (MediaPlayer + MediaPlaybackList).
  - Streaming with transcoding and bitrate limits, separate for metered and unmetered networks.
  - Downloads/offline cache, offline mode, replay gain, a 10-band equalizer (AudioGraph), playback rate, sleep timer and scrobbling.
  - Music and podcast modes, and radio streams with ICY titles.
- **Player UI:**
  - Player bar, queue pane (drag to reorder), synced lyrics pane and a now playing page.
  - Mini player (compact overlay, always on top).
  - An app menu (the "…" button in the title bar) with the commands of the macOS main menu.
  - Keyboard shortcuts; F1 lists them (`PlayerKeyboardShortcuts`).
- **Windows integration:**
  - Media keys and the Windows media flyout (System Media Transport Controls).
  - Notifications for new podcast episodes and finished downloads.
  - Mica, light/dark mode, per-account accent color, single instance, and preventing screen lock.
  - `amperfy://x-callback-url/...` automation URLs, the same actions as the iOS app (play by ID, search and play, random songs, play/pause/next/previous, shuffle, repeat, offline mode, rating, favorite).
- **Settings:**
  - Account, display, sidebar and home, library, player and streaming, equalizer, artwork and notifications.
  - Support: event log, diagnostics export and usage statistics.
  - About: license and acknowledgements.

Not ported, because Windows has no equivalent: CarPlay, Siri/App Intents, Apple Watch, haptics and swipe gestures. Context menus and keyboard shortcuts replace the swipe gestures.

## Install
- **Installer (recommended):** download `AmperfyWin-win-x64-Setup.exe` (or `AmperfyWin-win-arm64-Setup.exe` for ARM devices) from the [latest release](https://github.com/april83c/amperfy-win/releases/latest) and run it.
  - It installs per user to `%LOCALAPPDATA%\AmperfyWin`, without admin rights; the app data stays in `%LOCALAPPDATA%\Amperfy`.
  - It sets up the .NET 10 Runtime if it's missing.
  - The installed app updates itself: it checks the GitHub releases after start and every few hours, downloads a new version in the background, and installs it when you close Amperfy or click "Restart now". You can also check manually in Settings > About.
- **Portable:** `AmperfyWin-win-x64-Portable.zip` / `AmperfyWin-win-arm64-Portable.zip` from a release, or the `Amperfy-win-x64` / `Amperfy-win-arm64` artifact of a CI run.
  - Unzip and start `Amperfy.exe`. CI artifacts don't update themselves.
  - These need the [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) for the matching architecture; if it's missing, Windows shows a prompt with a download link.
- The Windows App SDK is bundled.
- Requires Windows 10 version 2004 (build 19041) or later, or Windows 11.

Data is stored in `%LOCALAPPDATA%\Amperfy`: the database, settings, cache and `logs\amperfy.log`. Passwords are encrypted with DPAPI for the current Windows user.

## Build
```
dotnet publish windows/src/Amperfy.App/Amperfy.App.csproj -c Release -r win-x64 -p:Platform=x64 -o out/Amperfy
```
This needs the .NET 10 SDK on Windows; no Visual Studio workload is required. For ARM64, use `-r win-arm64 -p:Platform=ARM64`.

To publish a release, push a tag (e.g. `git tag v2.0.1 && git push origin v2.0.1`). The `Windows release` workflow then:
- builds x64 and ARM64 with that version;
- packs them with [Velopack](https://velopack.io) (installer, portable zip, full and delta update packages; channels `win-x64` / `win-arm64`);
- publishes a GitHub release.

The installed apps update from these releases. Tags with a suffix (e.g. `v2.1.0-beta.1`) become pre-releases, which installed apps don't update to.

Updates download from the public release URLs without credentials, so the repository must be public for the installer link and self-updates to work.

## Development
| Path | Content |
| --- | --- |
| `src/Amperfy.Core` | Cross-platform port of AmperfyKit: EF Core/SQLite storage, Subsonic and Ampache APIs, sync, downloads, player logic |
| `src/Amperfy.App` | WinUI 3 app: pages, controls, audio engine, system integration |
| `tests/Amperfy.Core.Tests` | xUnit tests; the ported Swift tests plus new ones |
| `scripts/` | CI and developer scripts |
| `docs/` | `ARCHITECTURE.md` (core conventions), `UI-GUIDE.md` (app conventions), `PORTING.md` (why C#), `PARITY.md` (feature parity with the Swift app) |

On Windows or Linux:
- `dotnet test windows/tests/Amperfy.Core.Tests` runs the core tests.
- Integration tests run against a live server: set `AMPERFY_IT_SERVER`, `AMPERFY_IT_USER` and `AMPERFY_IT_PASSWORD`. The server should serve `tests/TestLibrary`.

On Linux, `windows/scripts/linux-check.sh` compiles the app's C# against generated XAML stubs. XAML is only compiled on Windows.

CI (`.github/workflows/windows.yml`) runs:
- the core tests;
- integration tests against Navidrome in Docker;
- the Windows build;
- a launch smoke test;
- an end-to-end screenshot tour. The tour logs in to a Navidrome server with the test library, syncs, and visits every page and player view. The screenshots are uploaded as the `tour-results` artifact.

To take the tour locally:
```
set AMPERFY_DATA_DIR=%TEMP%\amperfy-tour
set AMPERFY_E2E_SERVER=http://127.0.0.1:4533
set AMPERFY_E2E_USER=admin
set AMPERFY_E2E_PASSWORD=...
Amperfy.exe --screenshot-tour %TEMP%\amperfy-tour\shots
```
