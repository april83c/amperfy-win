using Amperfy.Core.Common;
using Amperfy.Core.Model;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Amperfy.App.Services;

/// Shows event logger messages as InfoBars stacked at the bottom right of the main window
/// (replacement for the iOS notification banners). "Details" opens a dialog with the full text.
public sealed class AlertService : IAlertDisplayable
{
    private StackPanel? _host;
    private DispatcherQueue? _dispatcher;
    private readonly DialogService _dialogs;
    private const int MaxVisible = 3;

    public AlertService(DialogService dialogs) => _dialogs = dialogs;

    public void Attach(StackPanel host)
    {
        _host = host;
        _dispatcher = host.DispatcherQueue;
    }

    public void Display(string topic, string shortMessage, string detailMessage, LogEntryType logType)
    {
        _dispatcher?.TryEnqueue(() => Show(topic, shortMessage, detailMessage, logType));
    }

    public void ShowInfo(string title, string message) => Display(title, message, message, LogEntryType.Info);

    /// An information that stays longer than the event messages (e.g. the one-time welcome hints).
    public void ShowNotice(string title, string message) =>
        _dispatcher?.TryEnqueue(() => Show(title, message, message, LogEntryType.Info, TimeSpan.FromSeconds(30)));

    private void Show(string topic, string shortMessage, string detailMessage, LogEntryType logType, TimeSpan? duration = null)
    {
        if (_host is null) return;
        var bar = new InfoBar
        {
            Title = topic,
            Message = shortMessage,
            IsOpen = true,
            IsClosable = true,
            Severity = logType switch
            {
                LogEntryType.ApiError or LogEntryType.Error => InfoBarSeverity.Error,
                LogEntryType.Info => InfoBarSeverity.Informational,
                _ => InfoBarSeverity.Informational,
            },
            MaxWidth = 460,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        if (!string.IsNullOrEmpty(detailMessage) && detailMessage != shortMessage)
        {
            var details = new HyperlinkButton { Content = "Details" };
            details.Click += async (_, _) => await _dialogs.ShowMessageAsync(topic, detailMessage);
            bar.ActionButton = details;
        }
        bar.Closed += (_, _) => _host.Children.Remove(bar);
        _host.Children.Add(bar);
        while (_host.Children.Count > MaxVisible) _host.Children.RemoveAt(0);

        var timer = _dispatcher!.CreateTimer();
        timer.Interval = duration ?? TimeSpan.FromSeconds(logType is LogEntryType.Info ? 5 : 10);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => bar.IsOpen = false;
        timer.Start();
    }
}
