# Feature parity: Swift app → Windows port

This table compares the user-facing features of the Swift app (`../Amperfy`, `../AmperfyKit`) with the Windows port (`src/Amperfy.App`, `src/Amperfy.Core`).

Status values:
- **Done:** ported; the Windows interaction may differ.
- **Partial:** part of the feature is missing; see the notes.
- **Missing:** not ported.
- **N/A:** not applicable on Windows.

"(fixed)" marks items closed by the parity pass that produced this document.

**Summary:** 73 done (15 of them fixed in this pass), 2 partial, 2 missing, 12 N/A (89 items).

## Screens (`Amperfy/Screens/ViewController`)
| Swift | Windows | Status | Notes |
| --- | --- | --- | --- |
| `LoginVC` | `LoginPage` | Done | Includes the API selector, custom HTTP headers and adding an account. |
| `SyncVC` | `SyncPage` | Done | Shows progress; initial sync can be skipped. |
| `UpdateVC` | – | N/A | Migrates legacy CoreData libraries. A Windows database always starts at the newest library version. |
| `WelcomePopupPresenter`, `LibrarySyncPopupVC` | `ShellPage.ShowWelcomeInfoIfNeeded` | Done (fixed) | Shows the synchronization hint once, as an InfoBar. The notification authorization popup is N/A: Windows manages notifications in its own settings. |
| `SplitVC`, `SideBarVC`, `LibraryNavigatorConfigurator` | `ShellPage` | Done | The sidebar is edited in Settings > Sidebar & Home. |
| `TabBarVC`, `LibraryVC` | – | N/A | iPhone layout. The sidebar lists the library. |
| `HomeVC`, `HomeEditorVC`, `HomeManager` | `HomePage`, `HomeEditorDialog` | Done | |
| `SearchVC` | `SearchPage` | Done | Local and server search, categories and history. |
| `ArtistsVC`, `ArtistDetailVC` | `ArtistsPage`, `ArtistDetailPage` | Done | Sorting, All/Album Artists filter, cached-only filter, "Download …". |
| `AlbumsVC`, `AlbumsCollectionVC`, `AlbumsCommonVCInteractions`, `AlbumDetailVC` | `AlbumsPage`, `AlbumDetailPage` | Done | Sorting, list/grid, grid size, "Download …". |
| `SongsVC` | `SongsPage` | Done | Sort options follow the API and filter (starred date, date added). |
| `GenresVC`, `GenreDetailVC` | `GenresPage`, `GenreDetailPage` | Done | |
| `MusicFoldersVC`, `IndexesVC`, `DirectoriesVC` | `MusicFoldersPage`, `IndexesPage`, `DirectoryPage` | Done | |
| `PlaylistsVC`, `PlaylistDetailVC`, `PlaylistEditVC` | `PlaylistsPage`, `PlaylistDetailPage` | Done | Sorting, Sync All Playlists, edit mode (reorder, remove, rename). |
| `PlaylistSelectorVC`, `PlaylistAdd/*` | `AddToPlaylistDialog`, `PlaylistAddSongsDialog` | Done | Includes the duplicate handling. |
| `PodcastsVC`, `PodcastDetailVC` | `PodcastsPage`, `PodcastDetailPage` | Done | Podcasts or episodes by date. Episode rows show status, progress, remaining time and cache state. |
| `RadiosVC` | `RadiosPage` | Done | |
| `DownloadsVC` | `DownloadsPage` | Done | Clear finished, retry failed and cancel all. |
| `QueueVC` | `Controls/Player/QueueView` | Done (fixed) | Uses the shared entity context menu with the player index, plus Play Next, Add to Queue and Remove from Queue. |
| `LyricsVC` | `Controls/Player/LyricsView` | Done | Synced lyrics; click a line to seek; smooth scrolling setting. |
| `PlainDetailsVC` | `DialogHelper.ShowTextAsync`, `PlayerUi.ShowPlayerInfoAsync` | Done | Lyrics text, descriptions and player info. |
| `NotificationDetailVC` | `EventLogSettingsPage` | Done | |
| `EntityPreviewVC` (preview card) | – | N/A | Windows context menus have no preview card. The menu actions are listed under Context menu actions. |

## Player (`Amperfy/Screens/Player`)
| Swift | Windows | Status | Notes |
| --- | --- | --- | --- |
| `MiniPlayerView` (bar) | `Controls/PlayerBar` | Done (fixed) | Right-click on the current item opens its entity menu, as the Swift "more" button does. |
| `PopupPlayerVC`, `LargeCurrentlyPlayingPlayerView` | `NowPlayingPage` | Done (fixed) | Added the star rating of the current song (Show Star Rating; read-only offline). Right-click on the artwork or title opens the entity menu. |
| `PlayerControlView` options menu | `PlayerUi.CreatePlayerOptionsFlyout` | Done (fixed) | Clear player/user/context queue, sleep timer, playback rate, lyrics, autoplay, mode, scroll to current, player info. Added "Add Context Queue to Playlist". |
| Audio visualizer (`AudioVisualizerKit`, `AudioVisualizerViews`) | – | Missing | Needs a sample tap on the output. `MediaPlayer` has none; only the AudioGraph (EQ) path could provide one. The settings `IsPlayerVisualizerDisplayed` and `SelectedVisualizerType` exist but are unused. |
| `SeekableTimeSlider`, audio info, live label | `SeekBar` | Done | |
| `PlayerUIHandler` (commands, skip buttons, favorite, radio site) | `Services/Player/PlayerUi`, `PlayerTransportControls` | Done | |
| Mini player window (`MiniPlayerSceneDelegate` → full popup player) | `MiniPlayerWindow` | Done (fixed) | Compact overlay (always on top) or a small window. Added Queue and Lyrics toggles below the controls, and the entity menu on right-click. |
| Display style compact/large | Queue/lyrics side pane, `NowPlayingPage` | Done | |
| AirPlay button | – | N/A | Windows picks the output device in the system volume flyout. |

## macOS main menu and keyboard commands
| Swift | Windows | Status | Notes |
| --- | --- | --- | --- |
| Main menu (`AppDelegateMainMenuExtension`) | `Services/AppMenu`: the "…" button in the title bar | Done (fixed) | Has the Controls and View submenus, Settings, Keyboard Shortcuts, Report an issue and About. Shortcuts are shown next to the items. |
| File: Open/Close Player Window (⌘W) | – | N/A | There is one main window; Windows closes it with Alt+F4. |
| File: Switch Library/Mini Player | Ctrl+Shift+M, app menu, player bar | Done | |
| Settings… (⌘,) | Ctrl+, , app menu, sidebar | Done (fixed) | |
| Controls: Play/Pause, Stop, Next, Previous | Space / Ctrl+P, Ctrl+., Ctrl+Right, Ctrl+Left, app menu | Done | |
| Controls: Skip Forward/Backward (seconds) | Ctrl+Shift+Right/Left, app menu | Done | |
| Controls: Go to Current Song (⌘L) | Ctrl+L, app menu, player options | Done (fixed) | Now scrolls to the song in the album, or to the episode in the podcast. |
| Controls: Shuffle, Repeat, Playback Rate submenus | App menu, player controls, Ctrl+H, Ctrl+T | Done | |
| Controls: Sleep Timer | App menu, player bar, Settings | Done | |
| Controls: Switch Music/Podcast mode | Ctrl+M, app menu, player bar | Done | |
| Help: Report an issue on GitHub | App menu, Settings > Support | Done | |
| Find (⌘F), sidebar, inspector | Ctrl+F; title bar pane toggle; queue/lyrics side pane | Done | |
| `KeyboardCommands` (Space, ←/→, r, s, m, p, ⌘F) | `PlayerKeyboardShortcuts` | Done | Adapted to Windows: arrow keys stay list navigation, so the player uses Ctrl+Left/Right, and Ctrl+T/H/M and Ctrl+Shift+P replace r/s/m/p. |
| List key commands (Enter, j/k, h, Shift+arrows, Shift+Esc) | ListView keyboard navigation, Enter, Alt+Left, mouse back button | Done (fixed) | Added Alt+Left and the mouse back button for going back. |
| Tab keys 1/2/3 | – | N/A | These switch the iPhone tab bar. |
| Keyboard shortcut overview | `KeyboardShortcutsDialog` (F1, app menu, Settings > General) | Done (fixed) | Lists `PlayerKeyboardShortcuts.All` and `.App`. |

## Context menu actions (`EntityPreviewVC` / `EntityPreviewActionBuilder`)
| Action | Status | Notes |
| --- | --- | --- |
| Play, Shuffle, Instant Mix | Done | |
| Music Queue (insert/append context/user queue), Podcast Queue | Done | |
| Show Album / Artist / Podcast | Done | |
| Show Lyrics | Done (fixed) | For the playing song, this opens the player lyrics (now playing tab or side pane). Otherwise it opens a text dialog. |
| Show Episode / Podcast Description | Done | |
| Favorite, Rating | Done | |
| Add to Playlist, Download, Delete Cache, Delete on Server | Done | |
| Share | Done (fixed) | Share opens the Windows share sheet with the file named "Artist - Title.ext". Save a Copy opens a file dialog. Items that aren't cached are downloaded first (`PlayableShare`). |
| Go to Site (radios), Copy ID to Clipboard (detailed info) | Done | |
| Same menu in the queue, player bar, now playing and mini player | Done (fixed) | |

## Settings (`Amperfy/SwiftUI/Settings`)
| Swift | Windows | Status | Notes |
| --- | --- | --- | --- |
| General: version, offline mode, prevent screen lock | `GeneralSettingsPage` | Done | Also links to the keyboard shortcuts (fixed). |
| Display: appearance, theme color, mini player on top, skip buttons, lyrics scrolling, detailed info, durations, star rating, disable shuffle | `DisplaySettingsPage` | Done | |
| Display: Haptic Feedback | – | N/A | |
| Swipe actions (`Swipe/*`) | – | N/A | Context menus and shortcuts replace swipe actions. |
| Library: counts, background sync, auto cache, cache size and limit, download all, delete cache, resync, duplicates | `LibrarySettingsPage` | Done | |
| Player: ReplayGain, auto-cache, playback resume, manual playback, autoplay, streaming format/bitrate (WiFi/cellular), cache format | `PlayerSettingsPage` | Done | WiFi/cellular become unmetered/metered network. A sleep timer card was added. |
| Equalizer (presets, create/delete, 10 bands) | `EqualizerSettingsPage` | Done | |
| Artwork: download settings, display settings, download all, delete all | `ArtworkSettingsPage` | Done | |
| Account: URL, user, theme, auto cache newest, scrobble streamed, API versions, server URLs, custom headers, update password, resync, logout | `AccountSettingsPage` | Done | |
| Support: GitHub issue, event log | `SupportSettingsPage`, `EventLogSettingsPage` | Done | |
| Support: send feedback by email with the log attached | Diagnostics copy/export | Partial | Windows has no mail composer that can attach files. The diagnostics JSON can be copied or exported instead. |
| License, acknowledgements | `AboutSettingsPage` | Done | |
| X-Callback-URLs documentation | `SupportSettingsPage` | Done | |
| Developer (generate default artworks) | – | N/A | For developers only. |

## AmperfyKit features
| Swift | Windows (`Amperfy.Core` + services) | Status | Notes |
| --- | --- | --- | --- |
| Subsonic / Ampache APIs, API detection, legacy login | `Api/*` | Done | |
| Initial sync, background sync, auto-download of the newest songs and episodes | `Sync/*` | Done | |
| Downloads: songs, episodes, artworks, lyrics; cache limit | `Downloads/*` | Done | |
| Embedded artwork extraction | `EmbeddedArtworkExtractor` | Done | |
| Scrobbling (online, and the offline cache synced later) | `ScrobbleSyncer` | Done | |
| Player: gapless, queues, shuffle, repeat, rate, ReplayGain, EQ, autoplay (instant mix), resume, sleep timer | `Player/*`, `Services/Audio` | Done | |
| Skip intervals: music 10 s, podcasts 15 s back and 30 s forward | `PlayerFacade`, `PlayerUi` | Done | |
| Podcast episode status, progress and "delete on server" | `PodcastEpisode`, `PodcastEpisodeRow` | Done | |
| Now playing info / remote commands | `SystemMediaControls` (SMTC) | Done | |
| `AudioSessionHandler`: pause when headphones are disconnected | – | Missing | Windows media playback moves to the new default device. There is no reliable way to tell that the previous device was headphones. |
| `AudioSessionHandler`: interruptions (calls, Siri) | – | N/A | |
| `LocalNotificationManager` (new episodes) | `ToastNotificationService` | Done | Clicking the notification opens the podcast. |
| `QuickActionsHandler` (home screen shortcuts) | `amperfy://` x-callback URLs | Partial | The same actions are available as URLs. A taskbar Jump List needs package identity; the app is unpackaged. |
| `ShareSongAction` | `Library/PlayableShare` | Done (fixed) | |
| `DuplicateEntitiesResolver`, `LibraryUpdater` | `Sync/*` | Done | |
| `FuzzySearcher` (Siri intents) | – | N/A | |
| CarPlay, Siri/App Intents, Apple Watch, haptics | – | N/A | |
