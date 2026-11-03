namespace Salvarr.Arr;

/// <summary>What Radarr or Sonarr says about its recycle bin.</summary>
public sealed record BinSettings(
    string AppName,
    string RecycleBinPath,
    int CleanupDays,
    TimeSpan CleanupInterval,
    DateTime? LastCleanupUtc,
    DateTime? NextCleanupUtc);

/// <summary>When a file in a Radarr/Sonarr recycle bin will be deleted automatically.</summary>
public sealed record RetentionInfo(string AppName, int CleanupDays, DateTime? ExpiresUtc, DateTime? DeleteAtUtc)
{
    /// <summary>Cleanup is off (0 days): the file stays until someone removes it.</summary>
    public bool KeptForever => CleanupDays <= 0;
}

public static class Retention
{
    /// <summary>
    /// Radarr's and Sonarr's cleanup deletes a file once its modification time
    /// plus the cleanup days is in the past (they set the modification time to
    /// "now" when they move a file into the bin). The task runs on a schedule,
    /// so the file goes at the first run on or after that moment.
    /// </summary>
    public static RetentionInfo For(DateTime modifiedUtc, BinSettings settings)
    {
        if (settings.CleanupDays <= 0)
        {
            return new RetentionInfo(settings.AppName, settings.CleanupDays, null, null);
        }

        var expires = modifiedUtc.AddDays(settings.CleanupDays);
        return new RetentionInfo(settings.AppName, settings.CleanupDays, expires, NextRunAfter(expires, settings));
    }

    /// <summary>The first cleanup run at or after <paramref name="moment"/>, or null when the schedule is unknown.</summary>
    public static DateTime? NextRunAfter(DateTime moment, BinSettings settings)
    {
        if (settings.NextCleanupUtc is not { } next)
        {
            return null;
        }

        if (moment <= next || settings.CleanupInterval <= TimeSpan.Zero)
        {
            return next;
        }

        var runs = Math.Ceiling((moment - next) / settings.CleanupInterval);
        return next + settings.CleanupInterval * runs;
    }

    /// <summary>True when two paths name the same folder, ignoring a trailing slash.</summary>
    public static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        return string.Equals(Path.TrimEndingDirectorySeparator(a.Trim()), Path.TrimEndingDirectorySeparator(b.Trim()),
            StringComparison.Ordinal);
    }
}
