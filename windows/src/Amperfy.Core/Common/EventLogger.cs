using Amperfy.Core.Api;
using System.Text.Json;

namespace Amperfy.Core.Common;

public enum AmperfyLogStatusCode
{
    DownloadError = 1,
    PlayerError = 2,
    EmailError = 3,
    InternalError = 4,
    ConnectionError = 5,
    CommonError = 6,
    Info = 7,
}

/// Displays errors/info to the user (implemented by the app: InfoBar / dialog).
public interface IAlertDisplayable
{
    void Display(string topic, string shortMessage, string detailMessage, LogEntryType logType);
}

/// Logs events into the event log (database) and optionally displays them to the user.
public sealed class EventLogger
{
    public bool SuppressAlerts { get; set; }
    public IAlertDisplayable? AlertDisplayer { get; set; }
    private readonly LibraryStorage _library;

    public EventLogger(LibraryStorage library)
    {
        _library = library;
    }

    public void Debug(string topic, string message) =>
        Report(topic, AmperfyLogStatusCode.Info, message, message, LogEntryType.Debug, displayPopup: false);

    public void Info(string topic, string message, bool displayPopup = true) =>
        Report(topic, AmperfyLogStatusCode.Info, message, message, LogEntryType.Info, displayPopup);

    public void Info(string topic, AmperfyLogStatusCode statusCode, string message, bool displayPopup) =>
        Report(topic, statusCode, message, message, LogEntryType.Info, displayPopup);

    public void Error(string topic, AmperfyLogStatusCode statusCode, string message, bool displayPopup) =>
        Report(topic, statusCode, message, message, LogEntryType.Error, displayPopup);

    public void Error(string topic, AmperfyLogStatusCode statusCode, string shortMessage, string detailMessage, bool displayPopup) =>
        Report(topic, statusCode, shortMessage, detailMessage, LogEntryType.Error, displayPopup);

    private void Report(string topic, AmperfyLogStatusCode statusCode, string shortMessage, string detailMessage, LogEntryType logType, bool displayPopup) =>
        SaveAndDisplay(topic, logType, (int)statusCode, $"{topic}: {shortMessage}", displayPopup, shortMessage, detailMessage);

    public void Report(string topic, Exception error, bool displayPopup = true)
    {
        if (error is ResponseError responseError)
        {
            Report(topic, responseError, displayPopup);
            return;
        }
        if (error is OperationCanceledException) return;
        var message = error is HttpRequestException ? $"Connection error: {error.Message}" : error.Message;
        SaveAndDisplay(topic, LogEntryType.Error, 0, $"{topic}: {message}", displayPopup, message, error.ToString());
    }

    public void Report(string topic, ResponseError error, bool displayPopup)
    {
        var alertMessage = error.StatusCode > 0 ? $"Status code: {error.StatusCode}\n" : "";
        alertMessage += error.ErrorMessage;
        var detailMessage = alertMessage;
        if (error.CleansedUrl is { } url) detailMessage += $"\n\nURL:\n{url.Description}";
        var isInfoError = error.Type == ResponseErrorType.Resource;
        detailMessage += "\n\nError Content:\n" + JsonSerializer.Serialize(error.AsInfo(topic), new JsonSerializerOptions { WriteIndented = true });
        SaveAndDisplay(topic, isInfoError ? LogEntryType.Info : LogEntryType.ApiError, error.StatusCode, error.ErrorMessage, displayPopup, alertMessage, detailMessage);
    }

    private void SaveAndDisplay(string topic, LogEntryType logType, int statusCode, string logMessage, bool displayPopup, string popupMessage, string detailMessage)
    {
        AmperfyLog.Write(logType is LogEntryType.Error or LogEntryType.ApiError ? LogLevel.Error : LogLevel.Info, "EventLogger", logMessage);
        MainThread.Post(() =>
        {
            try
            {
                var entry = _library.CreateLogEntry();
                entry.Type = logType;
                entry.StatusCode = statusCode;
                entry.Message = logMessage;
                _library.SaveContext();
            }
            catch (Exception ex)
            {
                AmperfyLog.Error("EventLogger", $"Could not save log entry: {ex.Message}");
            }
            if (displayPopup && !SuppressAlerts) AlertDisplayer?.Display(topic, popupMessage, detailMessage, logType);
        });
    }
}
