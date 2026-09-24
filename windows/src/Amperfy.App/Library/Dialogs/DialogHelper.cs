using Amperfy.App.Services;
using Amperfy.Core.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Amperfy.App.Library.Dialogs;

/// Shows custom ContentDialogs of the library UI on the main window.
/// WinUI allows only one open ContentDialog; a second one is ignored (returns None).
public static class DialogHelper
{
    private static bool _isShowing;

    public static bool IsShowing => _isShowing;

    public static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        var services = AppServices.Instance;
        var root = services.MainWindow?.Content?.XamlRoot;
        if (root is null || _isShowing) return ContentDialogResult.None;
        dialog.XamlRoot = root;
        if (Ui.Style("DefaultContentDialogStyle") is { } style) dialog.Style = style;
        if (services.MainWindow?.Content is FrameworkElement rootElement) dialog.RequestedTheme = rootElement.ActualTheme;
        _isShowing = true;
        try
        {
            return await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            AmperfyLog.Error("DialogHelper", $"Dialog could not be shown: {ex.Message}");
            return ContentDialogResult.None;
        }
        finally
        {
            _isShowing = false;
        }
    }

    public static async Task<bool> ConfirmAsync(string title, string message, string confirmText, bool destructive = false)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = confirmText,
            CloseButtonText = "Cancel",
            DefaultButton = destructive ? ContentDialogButton.Close : ContentDialogButton.Primary,
        };
        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    /// Scrollable, selectable text (descriptions, lyrics).
    public static async Task ShowTextAsync(string title, string text, string? subtitle = null)
    {
        var panel = new StackPanel { Spacing = 8 };
        if (!string.IsNullOrEmpty(subtitle))
        {
            panel.Children.Add(new TextBlock
            {
                Text = subtitle,
                TextWrapping = TextWrapping.Wrap,
                Opacity = Ui.SecondaryOpacity,
            });
        }
        panel.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        });
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer { Content = panel, MaxHeight = 520, Padding = new Thickness(0, 0, 12, 0) },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
        };
        await ShowAsync(dialog);
    }

    public static async Task<string?> PromptTextAsync(string title, string placeholder, string initialText = "", string confirmText = "OK")
    {
        var box = new TextBox { PlaceholderText = placeholder, Text = initialText, MinWidth = 320 };
        var dialog = new ContentDialog
        {
            Title = title,
            Content = box,
            PrimaryButtonText = confirmText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        box.Loaded += (_, _) => { box.Focus(FocusState.Programmatic); box.SelectAll(); };
        return await ShowAsync(dialog) == ContentDialogResult.Primary ? box.Text : null;
    }
}
