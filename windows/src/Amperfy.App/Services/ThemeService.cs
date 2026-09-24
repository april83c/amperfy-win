using Amperfy.App.Helpers;
using Amperfy.Core.Common;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Amperfy.App.Services;

/// Applies the account theme color as app accent color and the appearance mode (light/dark/system)
/// (port of AppDelegate.setAppTheme / setAppAppearanceMode).
public static class ThemeService
{
    private static IDisposable? _accountSubscription;

    /// Accent color variants (Windows derives them from the system accent color).
    private static readonly string[] AccentColorKeys =
    [
        "SystemAccentColor",
        "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
        "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
    ];

    /// Accent brushes of the WinUI theme dictionaries and the accent color variant they use
    /// (light theme, dark theme). See Common_themeresources_any.xaml of WinUI.
    private static readonly (string Key, int Light, int Dark)[] AccentBrushes =
    [
        ("AccentFillColorDefaultBrush", 4, 2),
        ("AccentFillColorSecondaryBrush", 4, 2),
        ("AccentFillColorTertiaryBrush", 4, 2),
        ("AccentTextFillColorPrimaryBrush", 5, 3),
        ("AccentTextFillColorSecondaryBrush", 6, 3),
        ("AccentTextFillColorTertiaryBrush", 4, 2),
        ("AccentFillColorSelectedTextBackgroundBrush", 0, 0),
        ("SystemControlForegroundAccentBrush", 0, 0),
        ("SystemControlBackgroundAccentBrush", 0, 0),
        ("SystemControlHighlightAccentBrush", 0, 0),
        ("SystemControlHighlightAltAccentBrush", 0, 0),
        ("SystemControlDisabledAccentBrush", 0, 0),
        ("SystemAccentColorBrush", 0, 0),
    ];

    /// Applies the accent color of the active account and follows account switches. Call once at
    /// startup (before the main window is created, so the first frame already uses the colors).
    public static void Initialize(AppServices services)
    {
        ApplyAccentColor(services.ActiveTheme, refreshUi: false);
        _accountSubscription ??= services.Notifications.Register(AmperfyNotification.AccountActiveChanged,
            _ => ApplyAccentColor(services.ActiveTheme));
    }

    /// Replaces the app accent color with the color of the theme preference.
    public static void ApplyAccentColor(ThemePreference theme, bool refreshUi = true)
    {
        try
        {
            var variants = CreateVariants(theme.AccentColor());
            var resources = Application.Current.Resources;
            for (var i = 0; i < AccentColorKeys.Length; i++) resources[AccentColorKeys[i]] = variants[i];
            UpdateAccentBrushes(resources, variants, isDarkDictionary: null, depth: 0);
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning("ThemeService", $"Accent color could not be applied: {ex.Message}");
        }
        if (refreshUi) AppServices.Instance?.MainWindow?.RefreshThemeResources();
    }

    /// Applies the appearance mode to the main window (live).
    public static void ApplyAppearance(AppearanceMode mode)
    {
        AppServices.Instance?.MainWindow?.ApplyRequestedTheme(ToElementTheme(mode));
    }

    public static ElementTheme ToElementTheme(AppearanceMode mode) => mode switch
    {
        AppearanceMode.Light => ElementTheme.Light,
        AppearanceMode.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    /// Accent, Light1..3, Dark1..3 (same order as <see cref="AccentColorKeys"/>).
    public static Color[] CreateVariants(Color accent) =>
    [
        accent,
        Blend(accent, 255, 0.25), Blend(accent, 255, 0.5), Blend(accent, 255, 0.7),
        Blend(accent, 0, 0.2), Blend(accent, 0, 0.4), Blend(accent, 0, 0.6),
    ];

    private static Color Blend(Color c, byte target, double amount)
    {
        byte Mix(byte v) => (byte)Math.Clamp(Math.Round(v + (target - v) * amount), 0, 255);
        return Color.FromArgb(c.A, Mix(c.R), Mix(c.G), Mix(c.B));
    }

    /// Updates the accent brushes that are already instantiated in the theme dictionaries of the
    /// app resources (incl. XamlControlsResources). Controls reference these brush instances, so
    /// changing their color updates the visible UI.
    private static void UpdateAccentBrushes(ResourceDictionary dictionary, Color[] variants, bool? isDarkDictionary, int depth)
    {
        if (depth > 6) return;
        if (isDarkDictionary is { } isDark)
        {
            foreach (var (key, light, dark) in AccentBrushes)
            {
                try
                {
                    if (dictionary.TryGetValue(key, out var value) && value is SolidColorBrush brush)
                        brush.Color = variants[isDark ? dark : light];
                }
                catch
                {
                    // brush may be sealed/unavailable in this dictionary
                }
            }
        }
        foreach (var (themeKey, themeDictionary) in dictionary.ThemeDictionaries)
        {
            if (themeDictionary is not ResourceDictionary td || themeKey is not string name) continue;
            if (name == "HighContrast") continue;
            UpdateAccentBrushes(td, variants, name is "Dark" or "Default", depth + 1);
        }
        foreach (var merged in dictionary.MergedDictionaries)
        {
            UpdateAccentBrushes(merged, variants, isDarkDictionary, depth + 1);
        }
    }
}
