using Microsoft.Extensions.Options;
using Salvarr.Arr;
using Salvarr.Media;
using Salvarr.Options;

namespace Salvarr.Library;

/// <summary>Keeps the latest scan of the recycle bins and rescans on request.</summary>
public sealed class RecycleBinService(IOptions<SalvarrOptions> options, MediaProbe probe, ArrService arr,
    ILogger<RecycleBinService> logger)
{
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private ScanResult _latest = ScanResult.Empty;

    public SalvarrOptions Options => options.Value;

    public ScanResult Latest => _latest;

    public bool IsScanning => _scanLock.CurrentCount == 0;

    /// <summary>The latest scan, rescanning first when it is older than <paramref name="maxAge"/>.</summary>
    public async Task<ScanResult> GetAsync(TimeSpan maxAge)
    {
        if (DateTime.UtcNow - _latest.ScannedUtc <= maxAge)
        {
            return _latest;
        }

        return await RescanAsync();
    }

    public async Task<ScanResult> RescanAsync(bool clearProbeCache = false, bool refreshArr = false)
    {
        await _scanLock.WaitAsync();
        try
        {
            if (clearProbeCache)
            {
                probe.ClearCache();
            }

            var statuses = await arr.GetStatusesAsync(refreshArr || clearProbeCache);
            var extraBins = ArrBins(statuses).ToList();
            var scan = await Task.Run(() => RecycleBinScanner.Scan(options.Value, extraBins));
            var folderClocks = new Dictionary<string, DateTime>();
            foreach (var file in scan.Files)
            {
                file.Retention = ArrService.RetentionFor(file, statuses, f => ArrService.CleanupClock(f, folderClocks));
            }

            var result = new ScanResult
            {
                Bins = scan.Bins,
                Files = scan.Files,
                OtherFileCount = scan.OtherFileCount,
                OtherFileBytes = scan.OtherFileBytes,
                Problems = scan.Problems,
                ScannedUtc = scan.ScannedUtc,
                Elapsed = scan.Elapsed,
                ArrStatuses = statuses
            };
            logger.LogInformation("Scanned {Bins} recycle bins: {Files} video files in {Elapsed:0.0} s",
                result.Bins.Count, result.Files.Count, result.Elapsed.TotalSeconds);
            foreach (var problem in result.Problems)
            {
                logger.LogWarning("{Problem}", problem);
            }

            _latest = result;
            return result;
        }
        finally
        {
            _scanLock.Release();
        }
    }

    /// <summary>
    /// Radarr's and Sonarr's own bin folders, so they are listed even when they
    /// are not called .recycle. Only folders inside the matching library count.
    /// </summary>
    private IEnumerable<RecycleBin> ArrBins(IEnumerable<ArrStatus> statuses)
    {
        foreach (var status in statuses)
        {
            if (status.Settings?.RecycleBinPath is not { } path)
            {
                continue;
            }

            var kind = status.AppName == "Sonarr" ? LibraryKind.Series : LibraryKind.Movies;
            var library = options.Value.Libraries.FirstOrDefault(l => l.Kind == kind && IsInside(path, l.Path));
            if (library != null)
            {
                yield return new RecycleBin(library, Path.TrimEndingDirectorySeparator(path));
            }
        }
    }

    private static bool IsInside(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != "." && !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    /// <summary>A file from the latest scan. Only files the scan found can be opened.</summary>
    public async Task<RecycledFile?> FindAsync(string id)
    {
        var scan = await GetAsync(TimeSpan.FromMinutes(5));
        var file = scan.Files.FirstOrDefault(f => f.Id == id);
        if (file != null || PathId.Decode(id) is not { } path || !File.Exists(path))
        {
            return file;
        }

        // Moved into the bin since the last scan.
        scan = await RescanAsync();
        return scan.Files.FirstOrDefault(f => f.Id == id);
    }
}
