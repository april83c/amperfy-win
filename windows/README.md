# Amperfy for Windows

A native Windows port of [Amperfy](../README.md), the Subsonic/Navidrome/Ampache music player, in
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
- **Context menus:** Play, Shuffle, Instant Mix, queue insert/append, show album/artist, lyrics, favorite, rating, add to playlist, download, delete cache, delete on server, go to site, copy ID.
- **Playlists:** create, rename, reorder by drag & drop, remove items, add songs; synced to the server.
- **Player:**
  - Gapless playback (MediaPlayer + MediaPlaybackList).
  - Streaming with transcoding and bitrate limits, separate for metered and unmetered networks.
  - Downloads/offline cache, offline mode, replay gain, a 10-band equalizer (AudioGraph), playback rate, sleep timer and scrobbling.
  - Music and podcast modes, and radio streams with ICY titles.
- **Player UI:**
  - Player bar, queue pane (drag to reorder), synced lyrics pane and a now playing page.
  - Mini player (compact overlay, always on top).
  - Keyboard shortcuts; see `PlayerKeyboardShortcuts.All`.
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
Download `Amperfy-win-x64.zip` or `Amperfy-win-arm64.zip` from a release, or from the `Amperfy-win-x64` artifact of a CI run. Unzip it and start `Amperfy.exe`. It is self-contained and needs no installer or .NET runtime. Requires Windows 10 version 2004 (build 19041) or later, or Windows 11.

Data is stored in `%LOCALAPPDATA%\Amperfy`: the database, settings, cache and `logs\amperfy.log`. Passwords are encrypted with DPAPI for the current Windows user.

## Build
```
dotnet publish windows/src/Amperfy.App/Amperfy.App.csproj -c Release -r win-x64 -p:Platform=x64 -o out/Amperfy
```
This needs the .NET 10 SDK on Windows; no Visual Studio workload is required. For ARM64, use `-r win-arm64 -p:Platform=ARM64`.

To publish a release, run the `Windows release` workflow (manually with a tag, or by pushing a `win-v*` tag). It builds zips for x64 and ARM64 and attaches them to a GitHub release.

## Development
| Path | Content |
| --- | --- |
| `src/Amperfy.Core` | Cross-platform port of AmperfyKit: EF Core/SQLite storage, Subsonic and Ampache APIs, sync, downloads, player logic |
| `src/Amperfy.App` | WinUI 3 app: pages, controls, audio engine, system integration |
| `tests/Amperfy.Core.Tests` | xUnit tests; the ported Swift tests plus new ones |
| `scripts/` | CI and developer scripts |
| `docs/` | `ARCHITECTURE.md` (core conventions), `UI-GUIDE.md` (app conventions), `PORTING.md` (why C#) |

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
