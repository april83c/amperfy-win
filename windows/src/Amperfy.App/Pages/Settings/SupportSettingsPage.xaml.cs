using Amperfy.App.Services;
using Amperfy.Core.Common;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Amperfy.App.Pages.Settings;

/// Support (port of SupportSettingsView): issue link, event log, diagnostic information
/// (LogData, the iOS mail attachment) and the log files.
public sealed partial class SupportSettingsPage : Page
{
    public const string IssuesUrl = "https://github.com/BLeeEZ/amperfy/issues";

    private readonly AppServices _services = AppServices.Instance;

    public SupportSettingsPage()
    {
        InitializeComponent();
        ReportIssueCard.Click += (_, _) => SettingsUi.OpenUri(IssuesUrl);
        ToolTipService.SetToolTip(ReportIssueCard, IssuesUrl);
        EventLogCard.Click += (_, _) => Frame?.Navigate(typeof(EventLogSettingsPage), null, new EntranceNavigationTransitionInfo());
        CopyDiagnosticsButton.Click += (_, _) => SettingsUi.CopyToClipboard(CollectDiagnostics());
        ExportDiagnosticsButton.Click += async (_, _) =>
        {
            var path = await SettingsUi.SaveTextFileAsync($"AmperfyLog {DateTime.Now:yyyy-MM-dd HHmm}", "JSON", ".json", CollectDiagnostics());
            if (path is not null) _services.Alerts.ShowInfo("Diagnostic Information", $"Saved to {path}");
        };
        var logFolder = Path.GetDirectoryName(AppPaths.LogFile) ?? AppPaths.DataDirectory;
        LogFolderCard.Description = logFolder;
        OpenLogFolderButton.Click += (_, _) => SettingsUi.OpenFolder(logFolder);
        Loaded += (_, _) => Reload();
        FillUrlCommands();
    }

    /// Documentation of the amperfy:// automation URLs (port of the iOS X-Callback-URL documentation view).
    private void FillUrlCommands()
    {
        try
        {
            foreach (var docu in SystemIntegration.UrlCommandDocumentation)
            {
                var lines = new List<string> { docu.Description };
                foreach (var p in docu.Parameters)
                {
                    var mandatory = p.IsMandatory ? "mandatory" : $"optional, default: {p.DefaultIfNotGiven ?? "-"}";
                    lines.Add($"• {p.Name} ({p.Type}, {mandatory}): {p.Description}");
                }
                lines.AddRange(docu.ExampleUrls);
                var copy = new Button { Content = "Copy example" };
                var example = docu.ExampleUrls.FirstOrDefault() ?? "";
                copy.Click += (_, _) => SettingsUi.CopyToClipboard(example);
                UrlCommandsExpander.Items.Add(SettingsUi.Card(docu.Name, string.Join("\n", lines), content: copy));
            }
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning("Support", $"URL command documentation: {ex.Message}");
        }
    }

    private void Reload()
    {
        try
        {
            var count = _services.Library.Context.LogEntries.Count();
            EventLogCard.Description = count == 1 ? "1 entry" : $"{count} entries";
        }
        catch (Exception ex)
        {
            EventLogCard.Description = "";
            AmperfyLog.Warning("Support", $"Event log count: {ex.Message}");
        }

        var stats = _services.Kit.UserStatistics;
        StatisticsExpander.Items.Clear();
        void Add(string title, int value) => StatisticsExpander.Items.Add(SettingsUi.Card(title, content: SettingsUi.SecondaryText(value.ToString())));
        Add("App sessions started", stats.AppSessionsStartedCount);
        Add("Played songs", stats.PlayedSongsCount);
        Add("Played songs from cache", stats.PlayedSongFromCacheCount);
        Add("Played songs via stream", stats.PlayedSongViaStreamCount);
        Add("Played with shuffle on", stats.ActiveShuffleOnSongsCount);
        Add("Played with repeat all", stats.ActiveRepeatAllSongsCount);
        Add("Played with repeat single", stats.ActiveRepeatSingleSongsCount);
    }

    private string CollectDiagnostics()
    {
        try
        {
            return LogData.CollectInformation(_services.Settings, _services.Library, _services.Player, _services.Kit.UserStatistics,
                LogDeviceInfo.Current(AppPaths.DataDirectory)).ToJson();
        }
        catch (Exception ex)
        {
            _services.EventLogger.Report("Diagnostic Information", ex);
            return ex.ToString();
        }
    }
}
