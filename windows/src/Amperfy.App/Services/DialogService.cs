using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Amperfy.App.Services;

/// ContentDialog helpers (confirmation, text input, message).
public sealed class DialogService
{
    private XamlRoot? _xamlRoot;
    private bool _isShowing;

    public void Attach(XamlRoot xamlRoot) => _xamlRoot = xamlRoot;

    private async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        if (_xamlRoot is null || _isShowing) return ContentDialogResult.None;
        dialog.XamlRoot = _xamlRoot;
        dialog.Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"];
        // dialogs are hosted in a popup: follow the app's appearance mode (Settings > Display)
        if (_xamlRoot.Content is FrameworkElement root) dialog.RequestedTheme = root.ActualTheme;
        _isShowing = true;
        try { return await dialog.ShowAsync(); }
        finally { _isShowing = false; }
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        await ShowAsync(new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer { Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }, MaxHeight = 480 },
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
        });
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", bool destructive = false)
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

    public async Task<string?> PromptTextAsync(string title, string placeholder, string initialText = "", string confirmText = "OK")
    {
        var box = new TextBox { PlaceholderText = placeholder, Text = initialText, MinWidth = 300 };
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
