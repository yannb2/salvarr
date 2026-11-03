using System.Globalization;

namespace Salvarr.Media;

/// <summary>Turns numbers into the short text the pages show.</summary>
public static class Format
{
    public static string Size(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} B"
            : value.ToString(value >= 100 ? "0" : value >= 10 ? "0.0" : "0.00", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    public static string BitRate(long bitsPerSecond)
    {
        return bitsPerSecond >= 1_000_000
            ? (bitsPerSecond / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture) + " Mb/s"
            : (bitsPerSecond / 1000.0).ToString("0", CultureInfo.InvariantCulture) + " kb/s";
    }

    public static string Duration(TimeSpan? duration)
    {
        if (duration is not { } d)
        {
            return "";
        }

        return d.TotalHours >= 1
            ? $"{(int)d.TotalHours}:{d.Minutes:D2}:{d.Seconds:D2}"
            : $"{d.Minutes}:{d.Seconds:D2}";
    }

    public static string TimeAgo(DateTime utc, DateTime? nowUtc = null)
    {
        var span = (nowUtc ?? DateTime.UtcNow) - utc;
        if (span < TimeSpan.Zero)
        {
            return "just now";
        }

        return span.TotalMinutes switch
        {
            < 1 => "just now",
            < 60 => Plural((int)span.TotalMinutes, "minute"),
            < 60 * 24 => Plural((int)span.TotalHours, "hour"),
            < 60 * 24 * 60 => Plural((int)span.TotalDays, "day"),
            < 60 * 24 * 730 => Plural((int)(span.TotalDays / 30.44), "month"),
            _ => Plural((int)(span.TotalDays / 365.25), "year")
        };

        static string Plural(int n, string unit) => n == 1 ? $"1 {unit} ago" : $"{n} {unit}s ago";
    }

    public static string LocalDate(DateTime utc)
    {
        return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }
}
