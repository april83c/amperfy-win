namespace Amperfy.Core.Sync;

/// Swift: NotificationContentType
public enum NotificationContentType
{
    PodcastEpisode,
}

/// Swift: NotificationUserInfo keys
public static class NotificationUserInfo
{
    public const string Type = "type";
    public const string Account = "account";
    public const string Id = "id";
}

/// A local (toast) notification the app should show (platform-neutral replacement for UNNotificationRequest).
public sealed record LocalNotificationRequest(
    string Identifier,
    string Title,
    string Body,
    /// Absolute path of an image to attach (may be null).
    string? ImagePath = null,
    NotificationContentType? ContentType = null,
    /// Account ident of the element (Swift userInfo "account").
    string? AccountIdent = null,
    /// Server id of the element (Swift userInfo "id").
    string? ElementId = null)
{
    /// Swift: content.userInfo
    public IReadOnlyDictionary<string, string> UserInfo
    {
        get
        {
            var info = new Dictionary<string, string>();
            if (ContentType is { } type) info[NotificationUserInfo.Type] = type.ToString();
            if (AccountIdent is not null) info[NotificationUserInfo.Account] = AccountIdent;
            if (ElementId is not null) info[NotificationUserInfo.Id] = ElementId;
            return info;
        }
    }
}

public interface ILocalNotificationManager
{
    void Notify(PodcastEpisode podcastEpisode);
    void NotifyDebug(string title, string body);
}

/// Port of Common/LocalNotificationManager.swift. Instead of scheduling UNNotificationRequests it raises
/// <see cref="NotificationRequested"/>; the Windows app turns the requests into toast notifications.
/// There is no authorization step on Windows (the app decides with <see cref="IsEnabled"/>).
public sealed class LocalNotificationManager : ILocalNotificationManager
{
    private readonly AmperfySettings _settings;

    public LocalNotificationManager(AmperfySettings settings)
    {
        _settings = settings;
    }

    public bool IsEnabled { get; set; } = true;

    /// Raised on the calling (main) thread for every notification to show.
    public event Action<LocalNotificationRequest>? NotificationRequested;

    public void Notify(PodcastEpisode podcastEpisode)
    {
        if (!_settings.User.IsPodcastNotificationsEnabled) return;
        if (podcastEpisode.Account is not { } account) return;
        var identifier = $"account-{account.Ident}-podcast-{podcastEpisode.Podcast?.Id ?? "0"}-episode-{podcastEpisode.Id}";
        string? imagePath = null;
        try
        {
            var preference = _settings.Accounts.GetSetting(account.Info).ArtworkDisplayPreference;
            var path = podcastEpisode.ImagePath(preference) ?? podcastEpisode.Podcast?.ImagePath(preference);
            if (path is not null && File.Exists(path)) imagePath = path;
        }
        catch (Exception ex)
        {
            AmperfyLog.Error("LocalNotificationManager", $"Attachment Error: {ex.Message}");
        }
        Raise(new LocalNotificationRequest(identifier, podcastEpisode.CreatorName, podcastEpisode.Title, imagePath,
            NotificationContentType.PodcastEpisode, account.Ident, podcastEpisode.Id));
    }

    public void NotifyDebug(string title, string body) =>
        Raise(new LocalNotificationRequest(StringExtensions.GenerateRandomString(15), title, body));

    private void Raise(LocalNotificationRequest request)
    {
        if (!IsEnabled) return;
        try { NotificationRequested?.Invoke(request); }
        catch (Exception ex) { AmperfyLog.Error("LocalNotificationManager", $"Request Error: {ex.Message}"); }
    }
}
