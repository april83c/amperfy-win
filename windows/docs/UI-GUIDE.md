# Amperfy for Windows: UI conventions

This covers the WinUI 3 app in `src/Amperfy.App`. For the core library, see `ARCHITECTURE.md`.

## Layout
- `App.xaml(.cs)` starts the app: `AppServices.Initialize()`, then `MainWindow`.
- `MainWindow` holds the custom `TitleBar` (back button, pane toggle, search box), the root `Frame` and the alert host.
  - The root frame shows `LoginPage`, `SyncPage` or `ShellPage`.
- `Pages/ShellPage` holds:
  - the `NavigationView` sidebar: Search, Home, the library categories of `AccountSetting.LibraryDisplaySettings`, account switcher and Settings;
  - the content frame, driven by `AppServices.Navigation`;
  - an optional right side pane: `ShellPage.Current.ShowSidePane(element)`, or `null` to hide it;
  - the `Controls/PlayerBar` at the bottom.
- `Services/PageRegistry` maps a `LibraryDisplayType` or an entity to its page:
  - `PageRegistry.ForEntity(entity)` returns `(Type Page, object Parameter)?`.
  - `PageRegistry.ForLibraryType(type)` gives the category pages; the navigation parameter is the `LibraryDisplayType`.
  - Category pages implement `ILibraryCategoryPage` so the sidebar highlights them.
- `Services/E2ETour` is the CI screenshot tour: log in to Navidrome with the test library, sync, then visit every page.

## Services (`AppServices.Instance`)
| Member | Purpose |
| --- | --- |
| `Kit` (`AmperKit`) | Composition root of the core: login, sync, accounts, `GetMeta(accountInfo)` |
| `Library` (`LibraryStorage`) | EF Core storage (main thread only) |
| `Settings` (`AmperfySettings`) | `App`, `User`, `Accounts` (per-account `AccountSetting`, `ActiveSetting`); saves automatically |
| `ActiveAccount`, `ActiveMeta` | Active account and its `MetaManager` (`LibrarySyncer`, `BackendApi`, `PlayableDownloadManager`, `ArtworkDownloadManager`, …) |
| `Player` (`IPlayerFacade`), `PlayerComponents`, `SleepTimer` | App-wide player |
| `Navigation` | `Navigate(typeof(Page), parameter)`, `GoBack()` |
| `Dialogs` | `ShowMessageAsync`, `ConfirmAsync`, `PromptTextAsync` (ContentDialog) |
| `Alerts` | InfoBar toasts; `EventLogger` alerts are routed here automatically |
| `EventLogger` | `Report(topic, exception)`, `Info(...)`, `Error(...)` |
| `Notifications` (`EventNotificationHandler`) | `Register(AmperfyNotification.X, handler)` returns an `IDisposable`; dispose it on `Unloaded` / `OnNavigatedFrom` |
| `DetailInfo(DetailType)` | `DetailInfoType` for `IPlayableContainable.Info(...)` strings |

## Rules
- **Threading:** everything runs on the UI thread; `MainThread` is the UI `SynchronizationContext`.
  - Await `ILibrarySyncer` calls from UI code; results are applied on the UI thread.
  - Wrap calls in `try/catch` and report errors with `EventLogger.Report("Topic", ex)`.
- **Entities:** these are EF entities with lazy-loading proxies.
  - Queries only see saved data; call `Library.SaveContext()` after changing entities.
  - After a sync, re-query what you show.
- **Offline mode:** check `Settings.User.IsOfflineMode` before server calls, as Swift does.
  - In offline mode, show only cached content (`onlyCached` query parameters).
- **Port from Swift:** the Swift sources are in `../Amperfy/Screens` (UIKit) and `../Amperfy/SwiftUI` (settings).
  - Keep the features and texts. Adapt the interaction to Windows: context menus (right click), double click / Enter to play, keyboard accelerators, tooltips.
- **Icons:** Segoe Fluent Icons glyphs via `FontIcon`. Shared glyphs are in `Helpers/Icons.cs`; add page-specific ones in your own files.
- **Artwork:** `Controls/ArtworkImage` with `Entity` set to the entity. It picks the correct image or the themed placeholder, and handles playlists (4 images).
- **Theme:** the account accent color comes from `ThemeHelper.AccentColor(ThemePreference)`. Otherwise use WinUI theme resources (`{ThemeResource ...}`), Mica and the standard typography styles (`TitleTextBlockStyle`, `SubtitleTextBlockStyle`, `BodyStrongTextBlockStyle`, `CaptionTextBlockStyle`, …).

## XAML pitfalls (WinUI 3, not WPF/UWP)
The XAML compiler runs only in the Windows CI build. `scripts/linux-check.sh` compiles the C# against generated stubs and flags unknown element types, but it does **not** check attributes, bindings or resources. Be conservative:
- **Namespaces:** use `Microsoft.UI.Xaml.*`, never `Windows.UI.Xaml`. XAML namespaces use `xmlns:local="using:Amperfy.App.Controls"`.
- **No WPF features:** no `DataTrigger`, `Style.Triggers`, `Visibility="Hidden"`, `StringFormat`, `RelativeSource AncestorType`, `MultiBinding` or `DockPanel`. Use `VisualStateManager`, converters or code-behind instead.
- **`x:Bind`:**
  - A `DataTemplate` needs `x:DataType="model:Song"`, e.g. with `xmlns:model="using:Amperfy.Core.Model"`.
  - Bound types and properties must be public.
  - Paths are compile-time checked, and function bindings must be public/static methods.
  - The default mode is `OneTime`.
  - When in doubt, build the UI in code-behind or use small view-model classes.
- **Glyphs:** in XAML, write `Glyph="&#xE768;"` as an entity. In C#, use `"\uE768"`.
- **Resources:** `StaticResource` keys must exist in WinUI's generic theme or in `App.xaml` merged dictionaries. Put new shared styles in `Styles/*.xaml` and merge them in `App.xaml`.
- **`ListView` / `GridView`:** set `SelectionMode="None"` with `IsItemClickEnabled="True"` for navigation lists.
  - For context menus, use `ContextFlyout` on the item template root, or handle `RightTapped` / `ContextRequested` in code.
- **Pages need a XAML file:** every type passed to `Frame.Navigate` must be a XAML page (`.xaml` + `.xaml.cs`), so that the XAML compiler generates its type metadata. Navigating to a code-only `Page` subclass crashes the process natively.
- **Community toolkit:** CommunityToolkit.WinUI controls available: `SettingsCard`, `SettingsExpander` (`xmlns:toolkit="using:CommunityToolkit.WinUI.Controls"`), `Segmented`.

## Settings
- `Pages/SettingsPage` lists the sections; the section pages are in `Pages/Settings/*` (all XAML pages).
  - Open a section with `Navigate(typeof(SettingsPage), SettingsPage.AccountSection)` (see the `*Section` constants).
  - Section pages declare their cards in XAML (`SettingsCard`/`SettingsExpander`) and wire values and events in code-behind (`SettingsUi.Bind`).
- Settings are applied immediately. Side effects live in `Services/`:
  - `ThemeService`: account accent color (`ApplyAccentColor`, follows `AccountActiveChanged`) and appearance mode (`ApplyAppearance` → `MainWindow.ApplyRequestedTheme`).
  - `ScreenLockPreventionService`, `ToastNotificationService`, `MeteredConnectionDetector` (metered network = "cellular" streaming settings).
  - `SettingsBootstrap.Initialize/Shutdown` starts and stops them (called from `App`).
- Display preferences (`IsShowSongDuration`, `IsShowRating`, `IsShowMusicPlayerSkipButtons`, …) are only stored; pages and the player read them when they render.

## Player UI
- `Services/Player/PlayerUi` holds the shared player logic (port of `PlayerUIHandler`):
  - commands: `TogglePlayPause`, `Previous`/`Next` (skip in podcast mode), `SetVolume`, `ClearUserQueue`, …;
  - navigation: `ShowAlbum/ShowArtist(playable)`, `ShowEntity(entity)`;
  - panes and windows: `ToggleQueuePane`, `ToggleLyricsPane`, `ToggleNowPlaying`, `ToggleMiniPlayer`;
  - menus and buttons: `CreateSleepTimerMenuItem`, `CreatePlaybackRateMenuItem`, `CreatePlayerOptionsFlyout`, `CreateVolumeButton`.
- After changing the queues outside the player facade's notifying methods (remove, move, clear), call `PlayerUi.NotifyQueueModified()`.
- To observe the player, keep a `PlayerObserver` in a field, call `Register()` once, and set `IsActive` on `Loaded` / `Unloaded`. The player keeps its observers as weak references.
- Reusable controls live in `Controls/Player`: `PlayerTransportControls`, `SeekBar`, `QueueView`, `LyricsView` and `MiniPlayerWindow`. Create them in code.
- Pages you navigate to must be XAML pages (`.xaml` + `.xaml.cs`). `Frame.Navigate` to a code-only page crashes.
- Keyboard shortcuts are in `PlayerKeyboardShortcuts.All`. Media keys go through the system media transport controls (`Services/Audio/SystemMediaControls`).

## Library browsing UI (`Library/`, `Controls/Library/`)
- **Context menus / actions:** `Library/EntityActions` (port of `EntityPreviewActionBuilder`).
  - `EntityActions.CreateMenuFlyout(container, new EntityActionOptions { PlayContext, PlayerIndex, HostPageType, Changed, ExtraItems })` returns a `MenuFlyout` built when it opens (current favorite/rating/cache state, online/offline mode).
  - The queue view passes `PlayerIndex` ("Play" jumps to the queue entry, no queue actions).
  - Helpers: `Open(entity, scrollTo)` (detail page; songs play), `PlayContainerAsync`, `ToggleFavoriteAsync`, `SetRatingAsync`, `DownloadAsync`, `IsPlayable`, `PrefetchAsync`.
  - `EntityActions.ShowLyricsHandler` (`Action<Song>`) replaces the default lyrics dialog.
- **Rows / tiles:** code-built controls, used from XAML templates as `<controls:LibraryRow />` (picks `PlayableRow`, `PodcastEpisodeRow`, `EntityRow` or a section header) and `<controls:EntityTile />`.
  - Items are `LibraryItem(entity, LibraryListContext, index)`; the context holds the play context provider, host page, display style.
  - `LibraryListController` wires a ListView/GridView: click opens containers, double click / Enter plays, Shift+F10 context menu, `SetIncrementalSource(loader, count)` for big lists (`IncrementalItems`).
- **Headers:** `LibraryPageHeader` (category pages: title, info, Play/Shuffle, filter box, command bar), `DetailHeader` + `DetailListToolbar` (detail pages).
- **Dialogs:** `Library/Dialogs` (`AddToPlaylistDialog`, `PlaylistAddSongsDialog`, `HomeEditorDialog`, `DialogHelper`).
- **Events:** `LibraryEventHub` (player changes, finished downloads, entity changes, offline mode); `ArtworkLoader.Request(entity)` downloads missing artworks and refreshes `ArtworkImage`s when done.
- Theme brushes are not looked up in code (the app theme can differ from the application theme): secondary texts use `Ui.SecondaryOpacity`, accents `Ui.ThemeAccent`.

## Verifying changes
- `./scripts/linux-check.sh` compiles the app's C# on Linux; it must end with `Build succeeded`.
- `dotnet test tests/Amperfy.Core.Tests` runs the core tests.
- The Windows CI (`.github/workflows/windows.yml`) builds the real app, runs a launch smoke test and the E2E screenshot tour against Navidrome, and uploads the screenshots as artifacts.
