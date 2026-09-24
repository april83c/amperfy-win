using Amperfy.App.Pages;
using Amperfy.Core.Model;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml;

namespace Amperfy.App.Services;

/// CI end-to-end run: started with "--screenshot-tour &lt;dir&gt;". Captures the login page, and when
/// AMPERFY_E2E_SERVER / AMPERFY_E2E_USER / AMPERFY_E2E_PASSWORD are set logs in, runs the initial
/// sync and visits every page (library categories, detail pages, search, settings, player),
/// capturing a screenshot of each. The app exits afterwards.
public static class E2ETour
{
    public static bool TryStart(MainWindow window)
    {
        var tour = ScreenshotTour.FromCommandLine();
        if (tour is null) return false;
        var services = AppServices.Instance;
        var server = Environment.GetEnvironmentVariable("AMPERFY_E2E_SERVER");
        var user = Environment.GetEnvironmentVariable("AMPERFY_E2E_USER");
        var password = Environment.GetEnvironmentVariable("AMPERFY_E2E_PASSWORD");

        tour.AddStep("start", () => Task.CompletedTask);
        if (!services.Kit.IsLoggedIn && !string.IsNullOrEmpty(server))
        {
            tour.AddStep("sync", async () =>
            {
                var account = await services.Kit.LoginAsync(server, user ?? "", password ?? "", BackendApiType.NotDetected);
                window.ShowSync(account);
                await Task.Delay(300);
            });
            tour.AddStep("home", async () =>
            {
                if (!services.Kit.IsLoggedIn) throw new InvalidOperationException("Login failed, skipping the library tour");
                await WaitUntil(() => window.RootContentFrame.Content is ShellPage, TimeSpan.FromMinutes(3));
            });
        }
        tour.AddDynamicSteps(() => BuildLibrarySteps(services));
        _ = tour.RunAsync(window, () =>
        {
            CrashLog.Write("E2E tour finished, exiting");
            Application.Current.Exit();
        });
        return true;
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > end) throw new TimeoutException("E2E wait timed out");
            await Task.Delay(250);
        }
    }

    private static IEnumerable<(string Name, Func<Task> Action)> BuildLibrarySteps(AppServices services)
    {
        var account = services.ActiveAccount;
        if (account is null || services.MainWindow.RootContentFrame.Content is not ShellPage shell) yield break;
        var nav = services.Navigation;
        var library = services.Library;

        foreach (var type in Enum.GetValues<LibraryDisplayType>())
        {
            yield return ($"library-{type}", () =>
            {
                var (page, parameter) = PageRegistry.ForLibraryType(type);
                nav.Navigate(page, parameter);
                return Task.CompletedTask;
            });
        }

        yield return ("detail-artist", () =>
        {
            var artist = library.GetArtists(account).OrderByDescending(a => a.AlbumCount).FirstOrDefault();
            if (artist is not null) nav.Navigate(typeof(ArtistDetailPage), artist);
            return Task.CompletedTask;
        });
        yield return ("detail-album", () =>
        {
            var album = library.GetAlbums(account).OrderByDescending(a => a.SongCount).FirstOrDefault();
            if (album is not null) nav.Navigate(typeof(AlbumDetailPage), album);
            return Task.CompletedTask;
        });
        yield return ("detail-genre", () =>
        {
            var genre = library.GetGenres(account).FirstOrDefault();
            if (genre is not null) nav.Navigate(typeof(GenreDetailPage), genre);
            return Task.CompletedTask;
        });
        yield return ("detail-playlist", () =>
        {
            var playlist = library.GetPlaylists(account).FirstOrDefault();
            if (playlist is not null) nav.Navigate(typeof(PlaylistDetailPage), playlist);
            return Task.CompletedTask;
        });
        yield return ("detail-musicfolder", () =>
        {
            var folder = library.GetMusicFolders(account).FirstOrDefault();
            if (folder is not null) nav.Navigate(typeof(IndexesPage), folder);
            return Task.CompletedTask;
        });
        yield return ("search", () =>
        {
            services.MainWindow.SearchBoxControl.Text = "a";
            nav.Navigate(typeof(SearchPage), "a");
            return Task.CompletedTask;
        });
        yield return ("settings", () =>
        {
            nav.Navigate(typeof(SettingsPage));
            return Task.CompletedTask;
        });
        yield return ("keyboard-shortcuts", async () =>
        {
            // ContentDialogs are not part of the captured window content: open, render and close it
            _ = KeyboardShortcutsDialog.ShowAsync();
            await Task.Delay(1500);
            KeyboardShortcutsDialog.Hide();
        });
        foreach (var step in PlayerSteps(services)) yield return step;
        _ = shell;
    }

    /// Player related steps: play the first album (player bar), queue pane, lyrics pane, now playing page and the
    /// mini player window (captured separately, as it is its own window).
    private static IEnumerable<(string Name, Func<Task> Action)> PlayerSteps(AppServices services)
    {
        var nav = services.Navigation;
        yield return ("player-bar", async () =>
        {
            if (services.ActiveAccount is not { } account) return;
            var album = services.Library.GetAlbums(account).FirstOrDefault(a => a.SongCount > 0)
                        ?? services.Library.GetAlbums(account).FirstOrDefault();
            if (album is null) return;
            if (album.Songs.Count == 0)
            {
                try { await services.Kit.GetMeta(account.Info).LibrarySyncer.SyncAsync(album); }
                catch (Exception ex) { CrashLog.Write($"E2E album sync failed: {ex.Message}"); }
            }
            services.Player.Play(new Amperfy.Core.Player.PlayContext(album));
            try
            {
                await WaitUntil(() => services.Player.IsPlaying && services.Player.ElapsedTime > 0.5, TimeSpan.FromSeconds(20));
            }
            catch (TimeoutException)
            {
                CrashLog.Write($"E2E: playback did not start (playing: {services.Player.IsPlaying}, item: {services.Player.CurrentlyPlaying?.Title})");
            }
        });
        yield return ("player-queue", () =>
        {
            Player.PlayerUi.ShowQueuePane(true);
            return Task.CompletedTask;
        });
        yield return ("player-lyrics", () =>
        {
            Player.PlayerUi.ShowLyricsPane(true);
            return Task.CompletedTask;
        });
        yield return ("player-now-playing", () =>
        {
            Player.PlayerUi.ShowLyricsPane(false);
            nav.Navigate(typeof(NowPlayingPage));
            return Task.CompletedTask;
        });
        yield return ("player-mini", async () =>
        {
            var mini = Controls.Player.MiniPlayerWindow.Show(minimizeMainWindow: false);
            await Task.Delay(2500);
            if (ScreenshotTour.FromCommandLine() is { } tour) await tour.CaptureAsync(mini, "player-mini-window");
            mini.Close();
        });
    }
}
