using Amperfy.App.Services;
using Amperfy.Core.Common;
using Amperfy.Core.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Amperfy.App.Pages.Settings;

/// Row of the event log list.
public sealed class LogEntryItem
{
    public LogEntryItem(LogEntry entry)
    {
        Message = entry.Message;
        Type = entry.Type;
        StatusCode = entry.StatusCode;
        CreationDate = entry.CreationDate;
        var typeText = entry.Type.Description();
        if (entry.Type is LogEntryType.Error or LogEntryType.ApiError && entry.StatusCode > 1) typeText += $" · Status code {entry.StatusCode}";
        TypeText = typeText;
        DateText = DateTime.SpecifyKind(entry.CreationDate, DateTimeKind.Utc).ToLocalTime().ToString("G");
        TypeBrush = (Brush)Application.Current.Resources[entry.Type is LogEntryType.Error or LogEntryType.ApiError
            ? "SystemFillColorCriticalBrush"
            : "TextFillColorSecondaryBrush"];
    }

    public string Message { get; }
    public LogEntryType Type { get; }
    public int StatusCode { get; }
    public DateTime CreationDate { get; }
    public string TypeText { get; }
    public string DateText { get; }
    public Brush TypeBrush { get; }

    public string Details => $"{Message}\n\nType: {TypeText}\nDate: {DateText} ({CreationDate.AsIso8601String()})";
}

/// Event log (port of EventLogSettingsView / EventLogCellView): list of the log entries (newest
/// first), details, copy, export to a file, clear, open the log folder.
public sealed partial class EventLogSettingsPage : Page
{
    private readonly AppServices _services = AppServices.Instance;
    private List<LogEntry> _entries = [];

    public EventLogSettingsPage()
    {
        InitializeComponent();
        BackButton.Click += (_, _) =>
        {
            if (Frame is null) return;
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(SupportSettingsPage), null, new EntranceNavigationTransitionInfo());
        };
        RefreshButton.Click += (_, _) => Reload();
        CopyAllButton.Click += (_, _) => SettingsUi.CopyToClipboard(LogData.FormatLogEntries(_entries));
        ExportButton.Click += async (_, _) =>
        {
            var path = await SettingsUi.SaveTextFileAsync($"Amperfy event log {DateTime.Now:yyyy-MM-dd HHmm}", "Text", ".txt", LogData.FormatLogEntries(_entries));
            if (path is not null) _services.Alerts.ShowInfo("Event Log", $"Saved to {path}");
        };
        OpenFolderButton.Click += (_, _) => SettingsUi.OpenFolder(Path.GetDirectoryName(AppPaths.LogFile) ?? AppPaths.DataDirectory);
        ClearButton.Click += (_, _) => _ = ClearAsync();
        EntriesList.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is LogEntryItem item) _ = ShowDetailsAsync(item);
        };
        EntriesList.RightTapped += EntriesList_RightTapped;
        Loaded += (_, _) => Reload();
    }

    private void Reload()
    {
        try
        {
            _entries = _services.Library.GetAllLogEntries();
        }
        catch (Exception ex)
        {
            _entries = [];
            AmperfyLog.Warning("EventLog", $"Log entries could not be loaded: {ex.Message}");
        }
        EntriesList.ItemsSource = _entries.Select(e => new LogEntryItem(e)).ToList();
        CountText.Text = _entries.Count == 1 ? "1 entry" : $"{_entries.Count} entries";
        EmptyText.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CopyAllButton.IsEnabled = ExportButton.IsEnabled = ClearButton.IsEnabled = _entries.Count > 0;
    }

    private void EntriesList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not LogEntryItem item) return;
        var menu = new MenuFlyout();
        var copy = new MenuFlyoutItem { Text = "Copy to Clipboard", Icon = new FontIcon { Glyph = SettingsGlyphs.Copy } };
        copy.Click += (_, _) => SettingsUi.CopyToClipboard(item.Message);
        var details = new MenuFlyoutItem { Text = "Details", Icon = new FontIcon { Glyph = SettingsGlyphs.About } };
        details.Click += (_, _) => _ = ShowDetailsAsync(item);
        menu.Items.Add(copy);
        menu.Items.Add(details);
        menu.ShowAt(e.OriginalSource as FrameworkElement, e.GetPosition(e.OriginalSource as UIElement));
        e.Handled = true;
    }

    private async Task ShowDetailsAsync(LogEntryItem item)
    {
        var dialog = new ContentDialog
        {
            Title = item.TypeText,
            Content = new ScrollViewer
            {
                MaxHeight = 480,
                Content = new TextBlock { Text = item.Details, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            },
            PrimaryButtonText = "Copy to Clipboard",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await this.ShowDialogAsync(dialog) == ContentDialogResult.Primary) SettingsUi.CopyToClipboard(item.Message);
    }

    private async Task ClearAsync()
    {
        var confirmed = await _services.Dialogs.ConfirmAsync("Clear Event Log", "Delete all entries of the event log?", "Clear", destructive: true);
        if (!confirmed) return;
        try
        {
            _services.Library.DeleteAllLogEntries();
        }
        catch (Exception ex)
        {
            _services.EventLogger.Report("Event Log", ex);
        }
        Reload();
    }
}
