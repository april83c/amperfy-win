using Amperfy.Core.Common;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Amperfy.App.Services.Player;

/// A player keyboard shortcut (for display, e.g. in tool tips / help).
public sealed record PlayerShortcut(string Keys, string Description);

/// Global player keyboard shortcuts of a window (port of AppDelegateKeyboardCommands + the "Controls" menu of
/// AppDelegateMainMenuExtension, adapted to Windows conventions). Media keys are handled by the system media
/// transport controls (SystemMediaControls), not here.
public static class PlayerKeyboardShortcuts
{
    public static readonly IReadOnlyList<PlayerShortcut> All =
    [
        new("Space", "Play / Pause (when no text field or button has the focus)"),
        new("Ctrl+P", "Play / Pause"),
        new("Ctrl+.", "Stop"),
        new("Ctrl+Right", "Next track (podcasts: skip forward)"),
        new("Ctrl+Left", "Previous track / replay (podcasts: skip backward)"),
        new("Ctrl+Shift+Right", "Skip forward"),
        new("Ctrl+Shift+Left", "Skip backward"),
        new("Ctrl+Up / Ctrl+Down", "Volume up / down"),
        new("Ctrl+Shift+Down", "Mute / unmute"),
        new("Ctrl+H", "Shuffle"),
        new("Ctrl+T", "Repeat (off / all / single)"),
        new("Ctrl+M", "Switch music / podcast mode"),
        new("Ctrl+L", "Go to current song"),
        new("Ctrl+F", "Search"),
        new("Ctrl+Shift+P", "Open / close the now playing view (Esc closes it)"),
        new("Ctrl+Shift+Q", "Show / hide the queue"),
        new("Ctrl+Shift+L", "Show / hide the lyrics"),
        new("Ctrl+Shift+M", "Open / close the mini player"),
    ];

    private const VirtualKey PeriodKey = (VirtualKey)190; // VK_OEM_PERIOD

    /// Attaches the shortcuts to the window content. <paramref name="isEnabled"/> is checked for every key press
    /// (e.g. only while the main shell is shown).
    public static void Attach(Window window, Func<bool> isEnabled)
    {
        if (window.Content is not UIElement root) return;
        root.PreviewKeyDown += (_, e) =>
        {
            try
            {
                if (isEnabled() && Handle(e.Key, e.OriginalSource, window)) e.Handled = true;
            }
            catch (Exception ex)
            {
                AmperfyLog.Warning("Shortcuts", $"Shortcut failed: {ex.Message}");
            }
        };
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private static bool IsTextInput(object? source) =>
        source is TextBox or PasswordBox or RichEditBox or AutoSuggestBox or NumberBox;

    /// Elements that use the space key themselves.
    private static bool UsesSpace(object? source) =>
        IsTextInput(source) || source is ButtonBase or ToggleSwitch or ComboBox or Slider;

    private static bool Handle(VirtualKey key, object? source, Window window)
    {
        var ctrl = IsDown(VirtualKey.Control);
        var shift = IsDown(VirtualKey.Shift);
        var alt = IsDown(VirtualKey.Menu);
        if (alt || IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows)) return false;

        if (!ctrl)
        {
            if (key == VirtualKey.Space && !shift && !UsesSpace(source))
            {
                PlayerUi.TogglePlayPause();
                return true;
            }
            if (key == VirtualKey.Escape && !shift && !IsTextInput(source) && window is MainWindow && PlayerUi.IsNowPlayingVisible)
            {
                PlayerUi.ToggleNowPlaying();
                return true;
            }
            return false;
        }

        var inText = IsTextInput(source);
        switch (key)
        {
            case VirtualKey.Right when !inText:
                if (shift) PlayerUi.SkipForward();
                else PlayerUi.Next();
                return true;
            case VirtualKey.Left when !inText:
                if (shift) PlayerUi.SkipBackward();
                else PlayerUi.Previous();
                return true;
            case VirtualKey.Up when !inText && !shift:
                PlayerUi.ChangeVolume(0.05f);
                return true;
            case VirtualKey.Down when !inText:
                if (shift) PlayerUi.ToggleMute();
                else PlayerUi.ChangeVolume(-0.05f);
                return true;
            case VirtualKey.P when !shift:
                PlayerUi.TogglePlayPause();
                return true;
            case PeriodKey when !shift:
                PlayerUi.Stop();
                return true;
            case VirtualKey.H when !shift:
                PlayerUi.ToggleShuffle();
                return true;
            case VirtualKey.T when !shift:
                PlayerUi.CycleRepeat();
                return true;
            case VirtualKey.M when !shift:
                PlayerUi.SwitchPlayerMode();
                return true;
            case VirtualKey.M when shift:
                PlayerUi.ToggleMiniPlayer();
                return true;
            case VirtualKey.L when !shift:
                PlayerUi.GoToCurrent();
                return true;
            case VirtualKey.L when shift:
                PlayerUi.ToggleLyricsPane();
                return true;
            case VirtualKey.Q when shift:
                PlayerUi.ToggleQueuePane();
                return true;
            case VirtualKey.P when shift:
                PlayerUi.ToggleNowPlaying();
                return true;
            case VirtualKey.F when !shift && window is MainWindow main && main.SearchBoxControl.Visibility == Visibility.Visible:
                main.SearchBoxControl.Focus(FocusState.Keyboard);
                return true;
        }
        return false;
    }
}
