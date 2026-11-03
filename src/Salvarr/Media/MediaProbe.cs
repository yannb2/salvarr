using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Options;
using Salvarr.Options;

namespace Salvarr.Media;

public sealed record ProbeResult(MediaDetails? Details, string? Error, string? RawJson);

/// <summary>
/// Reads a file with ffprobe (and mediainfo for the raw view). Results are
/// cached by path, size and modification time, so opening a page twice does
/// not read the disk twice.
/// </summary>
public sealed class MediaProbe(IOptions<SalvarrOptions> options, ILogger<MediaProbe> logger)
{
    public const string FfprobeExecutable = "ffprobe";
    public const string MediaInfoExecutable = "mediainfo";

    /// <summary>An 80 GB remux takes several minutes to read from disks.</summary>
    private static readonly TimeSpan WholeFilmTimeout = TimeSpan.FromMinutes(60);

    private readonly ConcurrentDictionary<string, Task<ProbeResult>> _probes = new();
    private readonly ConcurrentDictionary<string, Task<string>> _mediaInfo = new();
    private readonly ConcurrentDictionary<string, Task<DolbyVisionResult>> _wholeFilm = new();

    public Task<ProbeResult> ProbeAsync(string path)
    {
        var key = CacheKey(path);
        var task = _probes.GetOrAdd(key, _ => ProbeUncachedAsync(path));

        // A failed probe (file busy, tool missing) is retried next time.
        if (task.IsCompleted && task.Result.Details == null)
        {
            _probes.TryRemove(key, out _);
        }

        return task;
    }

    public Task<string> MediaInfoAsync(string path)
    {
        return _mediaInfo.GetOrAdd(CacheKey(path), _ => MediaInfoUncachedAsync(path));
    }

    /// <summary>
    /// The Dolby Vision RPU of every frame, for the brightness of the whole film
    /// (L1 MaxCLL and MaxFALL) and the scene count. Reads the whole file.
    /// </summary>
    public Task<DolbyVisionResult> WholeFilmDolbyVisionAsync(string path)
    {
        var key = CacheKey(path);
        var task = _wholeFilm.GetOrAdd(key, _ => DoviTool.ReadAsync(path, null, WholeFilmTimeout));
        if (task.IsCompleted && task.Result.Details == null)
        {
            _wholeFilm.TryRemove(key, out _);
        }

        return task;
    }

    /// <summary>The whole-film result when it was already read, without reading.</summary>
    public DolbyVisionDetails? CachedWholeFilm(string path)
    {
        return _wholeFilm.TryGetValue(CacheKey(path), out var task) && task.IsCompletedSuccessfully
            ? task.Result.Details
            : null;
    }

    /// <summary>Forgets everything, e.g. after a refresh. A whole-film read is kept: it is slow and the key has the file's size and date.</summary>
    public void ClearCache()
    {
        _probes.Clear();
        _mediaInfo.Clear();
    }

    private async Task<ProbeResult> ProbeUncachedAsync(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return new ProbeResult(null, "The file no longer exists.", null);
        }

        var result = await ProcessRunner.RunAsync(FfprobeExecutable,
            ["-v", "error", "-print_format", "json", "-show_streams", "-show_chapters", "-show_format", path],
            TimeSpan.FromSeconds(60));

        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
        {
            logger.LogWarning("ffprobe failed on {Path}: {Error}", path, result.Error);
            return new ProbeResult(null, $"ffprobe could not read the file: {result.Error.Trim()}", result.Output);
        }

        MediaDetails details;
        try
        {
            var probe = MediaParser.Deserialize(result.Output) ?? new FFprobeResult();
            details = MediaParser.Parse(path, file.Length, probe);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not read ffprobe output for {Path}", path);
            return new ProbeResult(null, $"Could not read ffprobe's output: {e.Message}", result.Output);
        }

        if (options.Value.DetectHdr10Plus)
        {
            foreach (var video in details.Video.Where(v => v.HdrFormat == "HDR10"))
            {
                if (await HasHdr10PlusAsync(path, video.Index) == true)
                {
                    video.HdrFormat = "HDR10+";
                }
            }
        }

        if (options.Value.ReadDolbyVision && details.HasDolbyVision)
        {
            var dv = await DoviTool.ReadAsync(path, DoviTool.SampleFrames, TimeSpan.FromMinutes(2));
            details.DolbyVision = dv.Details;
            details.DolbyVisionError = dv.Error;
            if (dv.Error != null)
            {
                logger.LogWarning("dovi_tool failed on {Path}: {Error}", path, dv.Error);
            }

            if (dv.Details?.Layer is { } layer)
            {
                foreach (var video in details.Video.Where(v => v.HasEnhancementLayer))
                {
                    video.EnhancementLayer = layer;
                }
            }
        }

        return new ProbeResult(details, null, result.Output);
    }

    /// <summary>
    /// HDR10+ is per-frame metadata, not in the stream header, so this decodes
    /// the first few frames. Null when ffprobe fails.
    /// </summary>
    private static async Task<bool?> HasHdr10PlusAsync(string path, int streamIndex)
    {
        var result = await ProcessRunner.RunAsync(FfprobeExecutable,
        [
            "-v", "error", "-select_streams", streamIndex.ToString(CultureInfo.InvariantCulture),
            "-read_intervals", "%+#5", "-show_frames", "-show_entries", "frame=side_data_list",
            "-print_format", "json", path
        ], TimeSpan.FromSeconds(60));

        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
        {
            return null;
        }

        try
        {
            return MediaParser.HasHdr10Plus(MediaParser.DeserializeFrames(result.Output));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<string> MediaInfoUncachedAsync(string path)
    {
        var result = await ProcessRunner.RunAsync(MediaInfoExecutable, [path], TimeSpan.FromSeconds(60));
        return result.Success ? result.Output : $"mediainfo failed: {result.Error.Trim()}";
    }

    private static string CacheKey(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? $"{path}|{file.Length}|{file.LastWriteTimeUtc.Ticks}" : path;
        }
        catch (IOException)
        {
            return path;
        }
    }
}
