using System.Collections.ObjectModel;
using Amperfy.App.Helpers;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Amperfy.App.Library.Dialogs;

/// "Home Preferences" (port of HomeEditorVC): choose the visible home sections and their order
/// (check boxes, drag and drop or the up / down buttons).
public sealed class HomeEditorDialog
{
    private sealed class Entry
    {
        public required HomeSection Section { get; init; }
        public required CheckBox CheckBox { get; init; }
        public required ListViewItem Container { get; init; }
    }

    private readonly ObservableCollection<ListViewItem> _containers = [];
    private readonly List<Entry> _entries = [];
    private readonly ListView _list = new()
    {
        SelectionMode = ListViewSelectionMode.None,
        CanReorderItems = true,
        CanDragItems = true,
        AllowDrop = true,
        MaxHeight = 440,
    };

    private HomeEditorDialog(IReadOnlyList<HomeSection> current)
    {
        var ordered = current.Concat(Enum.GetValues<HomeSection>().Where(s => !current.Contains(s))).ToList();
        foreach (var section in ordered)
        {
            var checkBox = new CheckBox { Content = section.Title(), IsChecked = current.Contains(section), MinWidth = 0 };
            var grid = new Grid { ColumnSpacing = 4 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(checkBox);
            var container = new ListViewItem { Content = grid, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            var entry = new Entry { Section = section, CheckBox = checkBox, Container = container };
            var up = Ui.IconButton(LibraryGlyphs.Up, "Move up", (_, _) => Move(entry, -1), 12);
            var down = Ui.IconButton(LibraryGlyphs.Down, "Move down", (_, _) => Move(entry, 1), 12);
            Grid.SetColumn(up, 1);
            Grid.SetColumn(down, 2);
            grid.Children.Add(up);
            grid.Children.Add(down);
            _entries.Add(entry);
            _containers.Add(container);
        }
        _list.ItemsSource = _containers;
    }

    /// Shows the editor; returns the new ordered visible sections or null when canceled.
    public static async Task<List<HomeSection>?> ShowAsync(IReadOnlyList<HomeSection> current)
    {
        var editor = new HomeEditorDialog(current);
        var content = new StackPanel { Spacing = 8, MinWidth = 380 };
        content.Children.Add(Ui.Text("Choose and order the sections shown on the home page.", "BodyTextBlockStyle", secondary: true, maxLines: 2));
        content.Children.Add(editor._list);
        var dialog = new ContentDialog
        {
            Title = "Home Preferences",
            Content = content,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await DialogHelper.ShowAsync(dialog) != ContentDialogResult.Primary) return null;
        return editor._containers
            .Select(c => editor._entries.First(e => ReferenceEquals(e.Container, c)))
            .Where(e => e.CheckBox.IsChecked == true)
            .Select(e => e.Section)
            .ToList();
    }

    private void Move(Entry entry, int direction)
    {
        var index = _containers.IndexOf(entry.Container);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= _containers.Count) return;
        _containers.Move(index, target);
    }
}
