using System.Text.RegularExpressions;

namespace Amperfy.Core.Common;

public static partial class StringExtensions
{
    public const string DefaultSectionInitial = "?";
    private const string UppercaseAsciiLetters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static bool IsFoundBy(this string s, string searchText) =>
        s.Contains(searchText, StringComparison.CurrentCultureIgnoreCase);

    /// Section initial used for alphabetic grouping ("#" for digits, "&amp;" for non latin letters, "?" otherwise).
    public static string SectionInitial(this string s)
    {
        if (string.IsNullOrEmpty(s)) return "?";
        var firstLen = s.Length > 1 && char.IsSurrogatePair(s, 0) ? 2 : 1;
        var first = RemoveDiacritics(s[..firstLen]).ToUpperInvariant();
        if (first.Length == 0) return "?";
        var c = first[0];
        if (char.IsDigit(c)) return "#";
        if (UppercaseAsciiLetters.Contains(c)) return first[..1];
        if (char.IsLetter(first, 0)) return "&";
        return "?";
    }

    public static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private const string RandomLetters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public static string GenerateRandomString(int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++) chars[i] = RandomLetters[Random.Shared.Next(RandomLetters.Length)];
        return new string(chars);
    }

    public static string DeletingPrefix(this string s, string prefix) =>
        s.StartsWith(prefix, StringComparison.Ordinal) ? s[prefix.Length..] : s;

    /// "12.3 MB" -> bytes (decimal units, like ByteCountFormatter .decimal)
    public static long? AsByteCount(this string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        (string suffix, double factor)[] units = [(" GB", 1e9), (" MB", 1e6), (" KB", 1e3), (" B", 1)];
        foreach (var (suffix, factor) in units)
        {
            if (s.EndsWith(suffix, StringComparison.Ordinal) &&
                double.TryParse(s[..^suffix.Length], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return (long)(v * factor);
        }
        return null;
    }

    /// "hh:mm:ss" -> seconds
    public static int? AsDurationInSeconds(this string s)
    {
        var parts = s.Split(':');
        if (parts.Length != 3) return null;
        if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m) || !int.TryParse(parts[2], out var sec)) return null;
        return h * 60 * 60 + m * 60 + sec;
    }

    public static DateTime? AsIso8601Date(this string s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    /// Very small HTML to plain text conversion (podcast descriptions).
    public static string Html2String(this string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var text = html.Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</p>", "\n", StringComparison.OrdinalIgnoreCase);
        text = HtmlTagRegex().Replace(text, "");
        return System.Net.WebUtility.HtmlDecode(text).Trim();
    }
}

public static class NumberFormatExtensions
{
    /// 3:05 or 1:02:03
    public static string AsColonDurationString(this int seconds)
    {
        if (seconds < 0) seconds = 0;
        var hours = seconds / 3600;
        var minutes = (seconds - hours * 3600) / 60;
        var secs = seconds - hours * 3600 - minutes * 60;
        return hours > 0 ? $"{hours}:{minutes:00}:{secs:00}" : $"{minutes}:{secs:00}";
    }

    public static string AsColonDurationString(this double seconds) =>
        double.IsFinite(seconds) ? ((int)seconds).AsColonDurationString() : "--:--";

    /// "1h 5m" (hour, minute; abbreviated)
    public static string AsDurationShortString(this int seconds)
    {
        var h = seconds / 3600;
        var m = seconds % 3600 / 60;
        if (h > 0) return m > 0 ? $"{h}h {m}m" : $"{h}h";
        return $"{m}m";
    }

    /// "1h 5m 3s" (hour, minute, second; abbreviated)
    public static string AsDurationString(this int seconds)
    {
        var h = seconds / 3600;
        var m = seconds % 3600 / 60;
        var s = seconds % 60;
        var parts = new List<string>();
        if (h > 0) parts.Add($"{h}h");
        if (m > 0) parts.Add($"{m}m");
        if (s > 0 || parts.Count == 0) parts.Add($"{s}s");
        return string.Join(" ", parts);
    }

    public static string AsMinuteString(this int seconds) => $"{seconds / 60} min";

    public static string AsDayString(this int seconds)
    {
        var d = seconds / 86400;
        return d == 1 ? "1 day" : $"{d} days";
    }

    /// Decimal byte string ("12.3 MB")
    public static string AsByteString(this long bytes)
    {
        if (bytes < 1000) return $"{bytes} bytes";
        string[] units = ["KB", "MB", "GB", "TB"];
        double v = bytes;
        var i = -1;
        do { v /= 1000; i++; } while (v >= 1000 && i < units.Length - 1);
        return v.ToString(v >= 100 ? "0" : "0.#", CultureInfo.CurrentCulture) + " " + units[i];
    }
}

public static class DateExtensions
{
    public static string AsIso8601String(this DateTime d) => d.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    public static string AsShortDayMonthString(this DateTime d) => d.ToString("d. MMMM", CultureInfo.CurrentCulture);
    public static string AsShortHrMinString(this DateTime d) => d.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
}

public static class CollectionExtensions
{
    public static T? ElementAtOrNull<T>(this IReadOnlyList<T> list, int index) where T : class =>
        index >= 0 && index < list.Count ? list[index] : null;

    public static List<List<T>> Chunked<T>(this IReadOnlyList<T> list, int size)
    {
        var result = new List<List<T>>();
        for (var i = 0; i < list.Count; i += size) result.Add(list.Skip(i).Take(size).ToList());
        return result;
    }

    /// Picks n random elements.
    public static List<T> RandomPick<T>(this IReadOnlyList<T> list, int count)
    {
        var copy = list.ToArray();
        var n = Math.Min(count, copy.Length);
        for (var i = copy.Length - 1; i >= copy.Length - n; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }
        return copy.Skip(copy.Length - n).ToList();
    }

    public static List<T> Shuffled<T>(this IEnumerable<T> source)
    {
        var arr = source.ToArray();
        Random.Shared.Shuffle(arr);
        return arr.ToList();
    }
}

/// Fuzzy search: ranks items by how well their name matches (lower score = better match).
public static class FuzzySearcher
{
    public static List<T> FindBestMatch<T>(IEnumerable<T> items, Func<T, string> name, string search)
    {
        var s = search.Trim().ToLowerInvariant();
        if (s.Length == 0) return items.ToList();
        return items
            .Select(i => (item: i, score: Score(name(i).ToLowerInvariant(), s)))
            .Where(x => x.score < 1.0)
            .OrderBy(x => x.score)
            .Select(x => x.item)
            .ToList();
    }

    private static double Score(string text, string pattern)
    {
        if (text == pattern) return 0;
        if (text.StartsWith(pattern, StringComparison.Ordinal)) return 0.05;
        var idx = text.IndexOf(pattern, StringComparison.Ordinal);
        if (idx >= 0) return 0.1 + 0.2 * idx / Math.Max(1, text.Length);
        var distance = Levenshtein(text.Length > pattern.Length + 4 ? text[..(pattern.Length + 4)] : text, pattern);
        var normalized = (double)distance / Math.Max(pattern.Length, 1);
        return normalized <= 0.6 ? 0.3 + normalized : 1.0;
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        for (var j = 1; j <= b.Length; j++)
        {
            var cost = a[i - 1] == b[j - 1] ? 0 : 1;
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
        }
        return d[a.Length, b.Length];
    }
}
