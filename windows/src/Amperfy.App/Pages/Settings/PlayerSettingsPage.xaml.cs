using Amperfy.App.Services;
using Amperfy.Core.Common;
using Amperfy.Core.Model;
using Amperfy.Core.Player;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml.Controls;

namespace Amperfy.App.Pages.Settings;

/// Player, stream and scrobble settings (port of PlayerSettingsView + the scrobble option of
/// AccountSettingsView + the sleep timer menu). "WiFi"/"Cellular" are called unmetered/metered
/// network on Windows (NetworkMonitor maps a metered connection to cellular).
public sealed partial class PlayerSettingsPage : Page
{
    private readonly AppServices _services = AppServices.Instance;
    private bool _isUpdatingSleepTimer;

    private static readonly StreamingFormatPreference[] FormatValues =
        [StreamingFormatPreference.Mp3, StreamingFormatPreference.Raw, StreamingFormatPreference.ServerConfig];
    private static readonly StreamingMaxBitratePreference[] BitrateValues = Enum.GetValues<StreamingMaxBitratePreference>();
    private static readonly CacheTranscodingFormatPreference[] CacheFormatValues =
        [CacheTranscodingFormatPreference.Mp3, CacheTranscodingFormatPreference.Raw, CacheTranscodingFormatPreference.ServerConfig];

    public PlayerSettingsPage()
    {
        InitializeComponent();
        var user = _services.Settings.User;
        var player = _services.Player;

        ReplayGainToggle.Bind(user.IsReplayGainEnabled, v =>
        {
            user.IsReplayGainEnabled = v;
            player.UpdateReplayGainEnabled(v);
        });
        AutoCacheCard.Description =
            $"Played tracks (songs and podcast episodes) are cached automatically. The next {PlayerDownloadPreparationHandler.PreDownloadCount} tracks are downloaded in advance.";
        AutoCacheToggle.Bind(player.IsAutoCachePlayedItems, v => player.IsAutoCachePlayedItems = v);
        PlaybackResumeToggle.Bind(user.IsPlayerSongPlaybackResumeEnabled, v => user.IsPlayerSongPlaybackResumeEnabled = v);
        ManualPlaybackToggle.Bind(user.IsPlaybackStartOnlyOnPlay, v => user.IsPlaybackStartOnlyOnPlay = v);
        AutoplayToggle.Bind(user.IsAutoplayEnabled, v => user.IsAutoplayEnabled = v);

        UnmeteredFormatCombo.Bind(FormatValues, f => f.Description(), user.StreamingFormatWifiPreference, f =>
        {
            user.StreamingFormatWifiPreference = f;
            UpdateTranscodings();
        });
        MeteredFormatCombo.Bind(FormatValues, f => f.Description(), user.StreamingFormatCellularPreference, f =>
        {
            user.StreamingFormatCellularPreference = f;
            UpdateTranscodings();
        });
        UnmeteredBitrateCombo.Bind(BitrateValues, b => b.Description(), user.StreamingMaxBitrateWifiPreference, b =>
        {
            user.StreamingMaxBitrateWifiPreference = b;
            UpdateBitrates();
        });
        MeteredBitrateCombo.Bind(BitrateValues, b => b.Description(), user.StreamingMaxBitrateCellularPreference, b =>
        {
            user.StreamingMaxBitrateCellularPreference = b;
            UpdateBitrates();
        });
        CacheFormatCombo.Bind(CacheFormatValues, f => f.Description(), user.CacheTranscodingFormatPreference,
            f => user.CacheTranscodingFormatPreference = f);
        CacheFormatCard.Description =
            "Select a transcoding format for downloaded songs. Changes don't apply to already downloaded songs; delete the cache and download them again if needed." +
            (_services.ActiveApiType == ServerApiType.Ampache
                ? ""
                : " For 'Raw/Original', Amperfy uses the Subsonic API's 'download' action, which skips transcoding. Other formats use the 'stream' action, which requires a proper transcoding configuration on the server.");

        var network = _services.Kit.NetworkMonitor;
        NetworkInfoText.Text = !network.IsConnectedToNetwork
            ? "Currently not connected to a network."
            : network.IsCellular ? "Currently connected to a metered network." : "Currently connected to an unmetered network.";

        ScrobbleToggle.IsOn = _services.Settings.Accounts.ActiveSetting.IsScrobbleStreamedItems;
        ScrobbleCard.IsEnabled = _services.Settings.Accounts.Active is not null;
        ScrobbleToggle.Toggled += (_, _) =>
        {
            if (_services.Settings.Accounts.Active is { } info)
                _services.Settings.Accounts.UpdateSetting(info, s => s.IsScrobbleStreamedItems = ScrobbleToggle.IsOn);
        };

        SleepTimerCombo.Items.Add("Off");
        foreach (var option in SleepTimer.Options) SleepTimerCombo.Items.Add(option.Title);
        SleepTimerCombo.SelectionChanged += SleepTimerCombo_SelectionChanged;
        Loaded += (_, _) =>
        {
            _services.SleepTimer.StateChanged -= UpdateSleepTimer;
            _services.SleepTimer.StateChanged += UpdateSleepTimer;
            UpdateSleepTimer();
        };
        Unloaded += (_, _) => _services.SleepTimer.StateChanged -= UpdateSleepTimer;
    }

    private void UpdateTranscodings()
    {
        var user = _services.Settings.User;
        _services.Player.SetStreamingTranscodings(new StreamingTranscodings(user.StreamingFormatWifiPreference, user.StreamingFormatCellularPreference));
    }

    private void UpdateBitrates()
    {
        var user = _services.Settings.User;
        _services.Player.SetStreamingMaxBitrates(new StreamingMaxBitrates(user.StreamingMaxBitrateWifiPreference, user.StreamingMaxBitrateCellularPreference));
    }

    // --- sleep timer ---------------------------------------------------------------------------

    private SleepTimerOption? _activeOption;

    private void UpdateSleepTimer()
    {
        var timer = _services.SleepTimer;
        _isUpdatingSleepTimer = true;
        if (!timer.IsActive) _activeOption = null;
        else if (timer.IsEndOfTrackActive && !timer.IsTimerActive) _activeOption = SleepTimer.Options.First(o => o.IsEndOfTrack);
        else if (timer.FireDate is { } fireDate && (_activeOption is null || _activeOption.IsEndOfTrack))
        {
            // timer started elsewhere (e.g. player menu): show the smallest option covering the remaining time
            var remaining = fireDate - DateTime.UtcNow;
            _activeOption = SleepTimer.Options.Where(o => o.Duration is { } d && d >= remaining).OrderBy(o => o.Duration).FirstOrDefault()
                            ?? SleepTimer.Options[^1];
        }
        SleepTimerCombo.SelectedIndex = _activeOption is null ? 0 : SleepTimer.Options.ToList().IndexOf(_activeOption) + 1;
        _isUpdatingSleepTimer = false;
        SleepTimerCard.Description = timer.StatusDescription ?? "Pause playback after a time or at the end of the current song or podcast episode.";
    }

    private void SleepTimerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSleepTimer) return;
        var index = SleepTimerCombo.SelectedIndex;
        var timer = _services.SleepTimer;
        try
        {
            if (index <= 0)
            {
                _activeOption = null;
                timer.Deactivate();
            }
            else
            {
                var option = SleepTimer.Options[index - 1];
                _activeOption = option;
                // switching between a duration and "end of track" replaces the other mode
                if (option.IsEndOfTrack) timer.Deactivate();
                else if (timer.IsEndOfTrackActive) _services.Player.IsShouldPauseAfterFinishedPlaying = false;
                timer.Activate(option);
            }
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning("Settings", $"Sleep timer: {ex.Message}");
        }
        UpdateSleepTimer();
    }
}
