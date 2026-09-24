# Porting decision: C# / .NET 10 + WinUI 3 (not Swift for Windows)

## Question
Port Amperfy (Swift, ~92k lines: AmperfyKit core + UIKit/SwiftUI app) to Windows using the
modern "golden path" UI stack. Should we keep Swift (Swift for Windows) or port to another language?

## Options considered

### 1. Swift on Windows (+ swift-winrt / WinUI bindings)
* The Swift **toolchain** is officially supported on Windows (swift.org), and The Browser
  Company's `swift-winrt` can generate WinRT/WinUI projections.
* But almost none of what Amperfy uses exists there: **UIKit, SwiftUI, CoreData, AVFoundation,
  MediaPlayer, Combine, CryptoKit, os_log, URLSession background sessions, CarPlay, Intents**.
  Only Foundation (partially) is available. Third party deps (Alamofire, AudioStreaming,
  MarqueeLabel, NotificationBanner, …) are Apple-only.
* WinUI from Swift has no XAML compiler integration, no designer, no x:Bind, tiny community,
  and the main sponsor (The Browser Company, Arc for Windows) has deprioritized it.
* Result: the reusable part would be limited to pure logic (~10–15% of the code), while the
  toolchain risk would be high. Not the golden path.

### 2. C# / .NET + WinUI 3 (Windows App SDK) — **chosen**
* Microsoft's recommended ("golden path") stack for modern native Windows apps: WinUI 3,
  Fluent design, Mica, XAML + MVVM (CommunityToolkit.Mvvm), `Windows.Media.Playback` with
  System Media Transport Controls, toast notifications.
* Rich equivalents for every Apple framework used:
  CoreData → **EF Core + SQLite** (object graph, lazy loading, change tracking),
  Alamofire/URLSession → `HttpClient`, XMLParser → `XmlReader`,
  AVFoundation → `Windows.Media.Playback.MediaPlayer`, MPNowPlayingInfoCenter/
  MPRemoteCommandCenter → `SystemMediaTransportControls`, UserDefaults → JSON settings,
  Keychain → DPAPI.
* The core can be **cross-platform .NET** (`Amperfy.Core`, `net10.0`) and fully unit-tested on
  Linux/CI, with the Swift XCTest suite ported to xUnit.

### 3. Others (Electron/Tauri, Flutter, Uno/Avalonia)
Not "native golden path" WinUI; no advantage over option 2 for a Windows-only private port.

## Mapping of Swift modules

| Swift | Windows port |
|---|---|
| AmperfyKit/Storage (CoreData) | `Amperfy.Core/Model`, `Amperfy.Core/Storage` (EF Core) |
| AmperfyKit/Api (Subsonic, Ampache) | `Amperfy.Core/Api/...` |
| AmperfyKit/Download | `Amperfy.Core/Downloads` |
| AmperfyKit/Player | `Amperfy.Core/Player` (+ `Amperfy.App` audio backend on MediaPlayer) |
| Amperfy (UIKit/SwiftUI screens, macOS sidebar layout) | `Amperfy.App` (WinUI 3 pages) |
| CarPlay, Siri Intents, Haptics | not applicable on Windows (dropped) |
| MPNowPlayingInfoCenter / Remote Commands | System Media Transport Controls (media keys, overlay) |
| macOS mini player | compact overlay mini player window |
