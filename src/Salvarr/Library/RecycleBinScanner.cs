using System.Diagnostics;
using Mono.Unix.Native;
using Salvarr.Options;

namespace Salvarr.Library;

/// <summary>Finds the recycle bins in each library and lists what is in them.</summary>
public static class RecycleBinScanner
{
    /// <param name="extraBins">Bins found another way, e.g. Radarr's recycle bin setting.</param>
    public static ScanResult Scan(SalvarrOptions options, IEnumerable<RecycleBin>? extraBins = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var problems = new List<string>();
        var bins = new List<RecycleBin>();

        foreach (var library in options.Libraries)
        {
            if (!Directory.Exists(library.Path))
            {
                problems.Add($"{library.Name}: {library.Path} does not exist or is not mounted.");
                continue;
            }

            FindBins(library, library.Path, 1, options, bins, problems);
        }

        foreach (var extra in extraBins ?? [])
        {
            if (Directory.Exists(extra.Path) && bins.All(b => !Arr.Retention.SamePath(b.Path, extra.Path)))
            {
                bins.Add(extra);
            }
        }

        var videoExtensions = new HashSet<string>(options.VideoExtensions, StringComparer.OrdinalIgnoreCase);
        var files = new List<RecycledFile>();
        var otherCount = 0;
        long otherBytes = 0;

        foreach (var bin in bins)
        {
            try
            {
                var enumeration = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = 0
                };

                foreach (var path in Directory.EnumerateFiles(bin.Path, "*", enumeration))
                {
                    var info = new FileInfo(path);
                    if (!videoExtensions.Contains(info.Extension))
                    {
                        otherCount++;
                        otherBytes += info.Length;
                        continue;
                    }

                    files.Add(new RecycledFile
                    {
                        Id = PathId.Encode(path),
                        FullPath = path,
                        Bin = bin,
                        RelativePath = Path.GetRelativePath(bin.Path, path),
                        SizeBytes = info.Length,
                        ModifiedUtc = info.LastWriteTimeUtc,
                        DeletedUtc = DeletedTime(info)
                    });
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{bin.Path}: {e.Message}");
            }
        }

        var matcher = new LibraryMatcher(options, bins);
        foreach (var file in files)
        {
            file.Match = matcher.Match(file);
        }

        return new ScanResult
        {
            Bins = bins,
            Files = files.OrderByDescending(f => f.DeletedUtc).ToList(),
            OtherFileCount = otherCount,
            OtherFileBytes = otherBytes,
            Problems = problems,
            ScannedUtc = DateTime.UtcNow,
            Elapsed = stopwatch.Elapsed
        };
    }

    private static void FindBins(LibraryOptions library, string folder, int depth, SalvarrOptions options,
        List<RecycleBin> bins, List<string> problems)
    {
        List<string> children;
        try
        {
            children = Directory.EnumerateDirectories(folder).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problems.Add($"{folder}: {e.Message}");
            return;
        }

        foreach (var child in children)
        {
            var name = Path.GetFileName(child);
            if (options.RecycleFolderNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                bins.Add(new RecycleBin(library, child));
            }
            else if (depth < options.SearchDepth && !name.StartsWith('.'))
            {
                FindBins(library, child, depth + 1, options, bins, problems);
            }
        }
    }

    /// <summary>
    /// The later of ctime and mtime. Moving a file into the bin sets its ctime;
    /// Radarr and Sonarr also set the mtime to "now" so their cleanup knows the age.
    /// </summary>
    private static DateTime DeletedTime(FileInfo info)
    {
        var modified = info.LastWriteTimeUtc;
        if (OperatingSystem.IsWindows())
        {
            return modified;
        }

        try
        {
            // Syscall.stat, not UnixFileInfo.LastStatusChangeTimeUtc: the latter adds the
            // nanoseconds as if they were microseconds, putting the time up to 16 minutes ahead.
            if (Syscall.stat(info.FullName, out var stat) != 0)
            {
                return modified;
            }

            var changed = DateTimeOffset.FromUnixTimeSeconds(stat.st_ctime).UtcDateTime
                .AddTicks(stat.st_ctime_nsec / 100);
            return changed > modified ? changed : modified;
        }
        catch (Exception)
        {
            // Native helper missing: fall back to the modification time.
            return modified;
        }
    }
}
