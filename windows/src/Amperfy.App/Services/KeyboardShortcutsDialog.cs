using Amperfy.App.Library;
using Amperfy.App.Library.Dialogs;
using Amperfy.App.Services.Player;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Amperfy.App.Services;

/// Help dialog listing the keyboard shortcuts (F1, the app menu and Settings > General). Replaces the key
/// command lists of the macOS main menu.
public static class KeyboardShortcutsDialog
{
    public static async Task ShowAsync()
    {
        if (DialogHelper.IsShowing) return;
        var panel = new StackPanel { Spacing = 4, Padding = new Thickness(0, 0, 12, 0) };
        AddGroup(panel, "Player", PlayerKeyboardShortcuts.All, isFirst: true);
        AddGroup(panel, "App and library", PlayerKeyboardShortcuts.App, isFirst: false);
        panel.Children.Add(new TextBlock
        {
            Text = "Media keys and the Windows media flyout control the player too. The player shortcuts work in the mini player as well.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = Ui.SecondaryOpacity,
            Margin = new Thickness(0, 12, 0, 0),
        });
        var dialog = new ContentDialog
        {
            Title = "Keyboard Shortcuts",
            Content = new ScrollViewer { Content = panel, MaxHeight = 560 },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
        };
        _current = dialog;
        try { await DialogHelper.ShowAsync(dialog); }
        finally { if (_current == dialog) _current = null; }
    }

    /// Closes the dialog if it is open (E2E tour).
    public static void Hide() => _current?.Hide();

    private static ContentDialog? _current;

    private static void AddGroup(StackPanel panel, string title, IReadOnlyList<PlayerShortcut> shortcuts, bool isFirst)
    {
        var header = Ui.Text(title, "BodyStrongTextBlockStyle");
        header.Margin = new Thickness(0, isFirst ? 0 : 16, 0, 4);
        panel.Children.Add(header);
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < shortcuts.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var keys = new TextBlock { Text = shortcuts[i].Keys, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            var description = new TextBlock { Text = shortcuts[i].Description, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(keys, i);
            Grid.SetRow(description, i);
            Grid.SetColumn(description, 1);
            grid.Children.Add(keys);
            grid.Children.Add(description);
        }
        panel.Children.Add(grid);
    }
}
