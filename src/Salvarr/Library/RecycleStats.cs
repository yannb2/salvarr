namespace Salvarr.Library;

/// <summary>What /api/stats returns: totals over every recycle bin.</summary>
public sealed record RecycleStats(
    int Files,
    long TotalBytes,
    int Movies,
    int Episodes,
    int DeletedToday,
    int ExpiringSoon,
    int Bins,
    DateTime ScannedUtc,
    int ToolUpdates = 0)
{
    /// <summary><paramref name="nowUtc"/> sets "today" (last 24 h) and "soon" (deleted automatically within 24 h).</summary>
    public static RecycleStats From(ScanResult scan, DateTime nowUtc)
    {
        var day = TimeSpan.FromDays(1);
        return new RecycleStats(
            scan.Files.Count,
            scan.Files.Sum(f => f.SizeBytes) + scan.OtherFileBytes,
            scan.Files.Count(f => f.Bin.Library.Kind == Options.LibraryKind.Movies),
            scan.Files.Count(f => f.Bin.Library.Kind == Options.LibraryKind.Series),
            scan.Files.Count(f => nowUtc - f.DeletedUtc < day),
            scan.Files.Count(f => f.Retention?.DeleteAtUtc is { } at && at - nowUtc < day),
            scan.Bins.Count,
            scan.ScannedUtc);
    }
}
