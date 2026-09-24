namespace Amperfy.Core.Common;

/// File names for sharing / exporting cached songs and episodes (port of ShareSongAction.sanitizedFileName,
/// extended with the characters that Windows doesn't allow in file names).
public static class PlayableFileExport
{
    private static readonly char[] InvalidChars = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    /// "Artist - Title" with the characters that aren't allowed in file names replaced by "-".
    public static string SanitizedFileName(AbstractPlayable playable) => SanitizedFileName(playable.DisplayString);

    public static string SanitizedFileName(string name)
    {
        var chars = name.Select(c => InvalidChars.Contains(c) || char.IsControl(c) ? '-' : c).ToArray();
        // Windows doesn't allow names ending with a dot or a space
        var result = new string(chars).Trim().TrimEnd('.').TrimEnd();
        return result.Length == 0 ? "Amperfy" : result;
    }

    /// Export file name: the sanitized display name plus the extension of the cached file (if it has one).
    public static string ExportFileName(AbstractPlayable playable, string cachedFilePath) =>
        SanitizedFileName(playable) + Path.GetExtension(cachedFilePath);
}
