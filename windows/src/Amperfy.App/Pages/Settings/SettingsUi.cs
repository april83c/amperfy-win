using Amperfy.App.Services;
using Amperfy.Core.Common;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.System;

namespace Amperfy.App.Pages.Settings;

/// Small helpers for the settings pages (binding controls to settings, dialogs, clipboard, files).
public static class SettingsUi
{
    /// Sets the toggle state and calls <paramref name="onChanged"/> when the user toggles it.
    public static void Bind(this ToggleSwitch toggle, bool value, Action<bool> onChanged)
    {
        toggle.IsOn = value;
        toggle.Toggled += (_, _) => onChanged(toggle.IsOn);
    }

    /// Fills a combo box with the values and calls <paramref name="onChanged"/> when the user selects one.
    public static void Bind<T>(this ComboBox combo, IReadOnlyList<T> values, Func<T, string> text, T selected, Action<T> onChanged)
    {
        combo.Items.Clear();
        foreach (var value in values) combo.Items.Add(text(value));
        combo.SelectedIndex = IndexOf(values, selected);
        combo.SelectionChanged += (_, _) =>
        {
            var index = combo.SelectedIndex;
            if (index >= 0 && index < values.Count) onChanged(values[index]);
        };
    }

    /// Selects a value without raising the change callback logic of <see cref="Bind{T}"/> twice
    /// (the callback runs, but settings setters ignore unchanged values).
    public static void Select<T>(this ComboBox combo, IReadOnlyList<T> values, T selected) =>
        combo.SelectedIndex = IndexOf(values, selected);

    private static int IndexOf<T>(IReadOnlyList<T> values, T selected)
    {
        for (var i = 0; i < values.Count; i++)
            if (EqualityComparer<T>.Default.Equals(values[i], selected)) return i;
        return -1;
    }

    public static FontIcon Icon(string glyph) => new() { Glyph = glyph };

    public static TextBlock SecondaryText(string text) => new()
    {
        Text = text,
        IsTextSelectionEnabled = true,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Right,
        MaxWidth = 420,
        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
    };

    /// A settings card with an optional glyph and content (right side).
    public static SettingsCard Card(string header, string? description = null, string? glyph = null, object? content = null)
    {
        var card = new SettingsCard { Header = header, Content = content };
        if (description is not null) card.Description = description;
        if (glyph is not null) card.HeaderIcon = Icon(glyph);
        return card;
    }

    /// A clickable settings card (chevron on the right).
    public static SettingsCard ActionCard(string header, string? description, string? glyph, Action onClick)
    {
        var card = Card(header, description, glyph);
        card.IsClickEnabled = true;
        card.Click += (_, _) => onClick();
        return card;
    }

    public static Button Button(string text, Action onClick, bool isAccent = false)
    {
        var button = new Button { Content = text };
        if (isAccent) button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        button.Click += (_, _) => onClick();
        return button;
    }

    public static void CopyToClipboard(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            AppServices.Instance.Alerts.ShowInfo("Copied", "Copied to clipboard.");
        }
        catch (Exception ex)
        {
            AppServices.Instance.EventLogger.Report("Clipboard", ex);
        }
    }

    public static async void OpenUri(string uri)
    {
        try { await Launcher.LaunchUriAsync(new Uri(uri)); }
        catch (Exception ex) { AppServices.Instance.EventLogger.Report("Open Link", ex); }
    }

    public static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppServices.Instance.EventLogger.Report("Open Folder", ex);
        }
    }

    /// Shows a content dialog on the page's XamlRoot (returns None if another dialog is open).
    public static async Task<ContentDialogResult> ShowDialogAsync(this FrameworkElement owner, ContentDialog dialog)
    {
        dialog.XamlRoot = owner.XamlRoot;
        dialog.Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"];
        dialog.RequestedTheme = owner.ActualTheme;
        try
        {
            return await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            // only one ContentDialog can be open at a time
            AmperfyLog.Warning("Settings", $"Dialog could not be shown: {ex.Message}");
            return ContentDialogResult.None;
        }
    }

    /// Lets the user pick a file to save and writes the text into it (unpackaged app: the picker needs the window handle).
    public static async Task<string?> SaveTextFileAsync(string suggestedName, string fileTypeName, string extension, string content)
    {
        try
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = suggestedName,
            };
            picker.FileTypeChoices.Add(fileTypeName, new List<string> { extension });
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(AppServices.Instance.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSaveFileAsync();
            if (file is null) return null;
            await File.WriteAllTextAsync(file.Path, content);
            return file.Path;
        }
        catch (Exception ex)
        {
            AppServices.Instance.EventLogger.Report("Export", ex);
            return null;
        }
    }
}
