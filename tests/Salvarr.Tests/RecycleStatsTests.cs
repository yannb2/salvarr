using Salvarr.Arr;
using Salvarr.Library;
using Salvarr.Options;

namespace Salvarr.Tests;

[TestClass]
public sealed class RecycleStatsTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void Counts_files_sizes_and_what_goes_soon()
    {
        var movies = new RecycleBin(new LibraryOptions { Name = "Movies", Path = "/movies", Kind = LibraryKind.Movies }, "/movies/.recycle");
        var tv = new RecycleBin(new LibraryOptions { Name = "TV", Path = "/tv", Kind = LibraryKind.Series }, "/tv/.recycle");

        RecycledFile File(RecycleBin bin, long size, TimeSpan deletedAgo, TimeSpan? deleteIn) => new()
        {
            Id = Guid.NewGuid().ToString(), FullPath = bin.Path + "/x.mkv", RelativePath = "x.mkv", Bin = bin,
            SizeBytes = size, DeletedUtc = Now - deletedAgo,
            Retention = deleteIn is { } d ? new RetentionInfo("Radarr", 7, Now + d, Now + d) : null
        };

        var scan = new ScanResult
        {
            Bins = [movies, tv],
            Files =
            [
                File(movies, 100, TimeSpan.FromHours(2), TimeSpan.FromDays(6)),
                File(movies, 200, TimeSpan.FromDays(6.5), TimeSpan.FromHours(10)),
                File(tv, 50, TimeSpan.FromDays(3), null)
            ],
            OtherFileBytes = 5,
            ScannedUtc = Now
        };

        var stats = RecycleStats.From(scan, Now);

        Assert.AreEqual(3, stats.Files);
        Assert.AreEqual(355, stats.TotalBytes);
        Assert.AreEqual(2, stats.Movies);
        Assert.AreEqual(1, stats.Episodes);
        Assert.AreEqual(1, stats.DeletedToday);
        Assert.AreEqual(1, stats.ExpiringSoon);
        Assert.AreEqual(2, stats.Bins);
    }
}
