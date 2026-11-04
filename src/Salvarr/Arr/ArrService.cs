using Microsoft.Extensions.Options;
using Salvarr.Library;
using Salvarr.Options;

namespace Salvarr.Arr;

/// <summary>What Salvarr knows about one app: its bin settings, or why it could not ask.</summary>
public sealed record ArrStatus(string AppName, ArrOptions Options, BinSettings? Settings, string? Error)
{
    public bool IsConfigured => Options.IsConfigured;
}

/// <summary>Talks to Radarr and Sonarr. Their bin settings are cached for a few minutes.</summary>
public sealed class ArrService(IOptions<SalvarrOptions> options, IHttpClientFactory httpFactory, ILogger<ArrService> logger,
    LiveSettings? live = null)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<ArrStatus> _statuses = [];
    private DateTime _fetchedUtc = DateTime.MinValue;

    public async Task<IReadOnlyList<ArrStatus>> GetStatusesAsync(bool refresh = false)
    {
        await _lock.WaitAsync();
        try
        {
            if (!refresh && DateTime.UtcNow - _fetchedUtc < CacheFor)
            {
                return _statuses;
            }

            _statuses =
            [
                await FetchAsync("Radarr", Radarr),
                await FetchAsync("Sonarr", Sonarr)
            ];
            _fetchedUtc = DateTime.UtcNow;
            return _statuses;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>The status of the app that manages a library (Radarr for movies, Sonarr for shows).</summary>
    public static ArrStatus? StatusFor(IEnumerable<ArrStatus> statuses, LibraryOptions library)
    {
        var name = library.Kind == LibraryKind.Series ? "Sonarr" : "Radarr";
        return statuses.FirstOrDefault(s => s.AppName == name);
    }

    /// <summary>
    /// When the app will delete the file, or null when the file is not in that
    /// app's bin (a Samba bin, say) or the app is not set up.
    /// </summary>
    /// <param name="clock">The modification time the cleanup looks at, see <see cref="CleanupClock"/>.</param>
    public static RetentionInfo? RetentionFor(RecycledFile file, IEnumerable<ArrStatus> statuses, Func<RecycledFile, DateTime>? clock = null)
    {
        var settings = StatusFor(statuses, file.Bin.Library)?.Settings;
        return settings != null && Retention.SamePath(file.Bin.Path, settings.RecycleBinPath)
            ? Retention.For((clock ?? (f => f.ModifiedUtc))(file), settings)
            : null;
    }

    /// <summary>
    /// The time Radarr's and Sonarr's cleanup judges a file by. A file at the top
    /// of the bin goes by its own modification time. A folder at the top of the
    /// bin ("Alien (1979)") is deleted as a whole once its newest file has expired,
    /// so every file inside goes by that newest time.
    /// </summary>
    public static DateTime CleanupClock(RecycledFile file, Dictionary<string, DateTime> folderCache)
    {
        var separator = file.RelativePath.IndexOf(Path.DirectorySeparatorChar);
        if (separator < 0)
        {
            return file.ModifiedUtc;
        }

        var topFolder = Path.Combine(file.Bin.Path, file.RelativePath[..separator]);
        if (!folderCache.TryGetValue(topFolder, out var newest))
        {
            newest = file.ModifiedUtc;
            try
            {
                foreach (var path in Directory.EnumerateFiles(topFolder, "*", SearchOption.AllDirectories))
                {
                    var modified = File.GetLastWriteTimeUtc(path);
                    if (modified > newest)
                    {
                        newest = modified;
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }

            folderCache[topFolder] = newest;
        }

        return newest;
    }

    /// <summary>Asks the app to rescan the movie or show in <paramref name="titleFolder"/>. Returns what happened, in words.</summary>
    public async Task<string> RescanAsync(LibraryOptions library, string titleFolder)
    {
        var arrOptions = library.Kind == LibraryKind.Series ? Sonarr : Radarr;
        var appName = library.Kind == LibraryKind.Series ? "Sonarr" : "Radarr";
        if (!arrOptions.IsConfigured)
        {
            return $"{appName} is not set up in Salvarr, so rescan it by hand.";
        }

        try
        {
            var client = Client(appName, arrOptions);
            var item = (await client.GetItemsAsync()).FirstOrDefault(i => Retention.SamePath(i.Path, titleFolder));
            if (item == null)
            {
                return $"{appName} has nothing in {titleFolder}, so there was nothing to rescan.";
            }

            await client.RescanAsync(item.Id);
            logger.LogInformation("Asked {App} to rescan {Title} ({Id})", appName, item.Title, item.Id);
            return $"{appName} is rescanning {item.Title}.";
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "{App} rescan failed for {Folder}", appName, titleFolder);
            return $"{appName} could not be asked to rescan: {e.Message}";
        }
    }

    /// <summary>Radarr as saved on the Settings page, else from the environment variables.</summary>
    public ArrOptions Radarr => live?.Radarr ?? options.Value.Radarr;

    public ArrOptions Sonarr => live?.Sonarr ?? options.Value.Sonarr;

    /// <summary>The Test button: reads the bin settings with these values, without keeping them.</summary>
    public Task<ArrStatus> TestAsync(string appName, ArrOptions arrOptions) => FetchAsync(appName, arrOptions);

    /// <summary>Forget the cached statuses, after the Settings page saved new values.</summary>
    public void Invalidate() => _fetchedUtc = DateTime.MinValue;

    private async Task<ArrStatus> FetchAsync(string appName, ArrOptions arrOptions)
    {
        if (!arrOptions.IsConfigured)
        {
            return new ArrStatus(appName, arrOptions, null, null);
        }

        try
        {
            var client = Client(appName, arrOptions);
            var config = await client.GetMediaManagementAsync();
            if (string.IsNullOrWhiteSpace(config?.RecycleBin))
            {
                return new ArrStatus(appName, arrOptions, null,
                    $"The recycle bin is off in {appName} (Settings > Media Management), so it deletes files for good.");
            }

            var task = (await client.GetTasksAsync())
                .FirstOrDefault(t => string.Equals(t.TaskName, ScheduledTask.CleanUpRecycleBin, StringComparison.OrdinalIgnoreCase));

            var settings = new BinSettings(appName, config.RecycleBin, config.RecycleBinCleanupDays,
                TimeSpan.FromMinutes(task?.Interval ?? 24 * 60),
                Utc(task?.LastExecution), Utc(task?.NextExecution));
            return new ArrStatus(appName, arrOptions, settings, null);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not read the recycle bin settings from {App}", appName);
            return new ArrStatus(appName, arrOptions, null, $"Could not reach {appName}: {e.Message}");
        }
    }

    private ArrClient Client(string appName, ArrOptions arrOptions)
    {
        var http = httpFactory.CreateClient("arr");
        return new ArrClient(http, arrOptions, appName);
    }

    private static DateTime? Utc(DateTime? value)
    {
        return value is { } v ? v.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : v.ToUniversalTime() : null;
    }
}
