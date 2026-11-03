using Microsoft.Extensions.Options;
using Salvarr.Media;
using Salvarr.Options;

namespace Salvarr.Library;

/// <summary>Keeps the latest scan of the recycle bins and rescans on request.</summary>
public sealed class RecycleBinService(IOptions<SalvarrOptions> options, MediaProbe probe, ILogger<RecycleBinService> logger)
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

    public async Task<ScanResult> RescanAsync(bool clearProbeCache = false)
    {
        await _scanLock.WaitAsync();
        try
        {
            if (clearProbeCache)
            {
                probe.ClearCache();
            }

            var result = await Task.Run(() => RecycleBinScanner.Scan(options.Value));
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
