using System.Text.RegularExpressions;
using Salvarr.Options;

namespace Salvarr.Library;

/// <summary>
/// Finds the movie or show folder a recycled file came from, and the files in
/// that folder now. Works for both bin layouts:
/// Samba keeps the whole tree (.recycle/live-action/Alien (1979)/file.mkv),
/// Radarr and Sonarr keep only the title folder (.recycle/Alien (1979)/file.mkv).
/// </summary>
public sealed partial class LibraryMatcher
{
    private readonly Dictionary<string, List<string>> _titleFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _filesByFolder = new(StringComparer.Ordinal);
    private readonly HashSet<string> _videoExtensions;
    private readonly HashSet<string> _binPaths;

    public LibraryMatcher(SalvarrOptions options, IEnumerable<RecycleBin> bins)
    {
        _videoExtensions = new HashSet<string>(options.VideoExtensions, StringComparer.OrdinalIgnoreCase);
        _binPaths = new HashSet<string>(bins.Select(b => b.Path), StringComparer.Ordinal);

        foreach (var library in options.Libraries)
        {
            IndexTitleFolders(library.Path);
        }
    }

    /// <summary>Number of folders that can be matched, for the status page.</summary>
    public int TitleFolderCount => _titleFolders.Values.Sum(v => v.Count);

    public LibraryMatch Match(RecycledFile file)
    {
        var library = file.Bin.Library;
        var segments = file.RelativeFolder
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        // 1. A folder in the bin path that is a title folder in the library.
        for (var i = 0; i < segments.Length; i++)
        {
            var folder = PickFolder(segments[i], library,
                Path.Combine([file.Bin.ParentPath, ..segments[..(i + 1)]]));
            if (folder != null)
            {
                var original = Path.Combine([folder, ..segments[(i + 1)..], file.FileName]);
                return Build(file, folder, original);
            }
        }

        // 2. The title at the start of the file name: "Alien (1979) - Director's Cut [...]".
        var title = TitleFromFileName(file.FileName, library.Kind);
        if (title != null && PickFolder(title, library, null) is { } byName)
        {
            return Build(file, byName, Path.Combine(byName, file.FileName));
        }

        // 3. Nothing in the library any more. Samba's layout still says where it was.
        return new LibraryMatch { OriginalPath = Path.Combine(file.Bin.ParentPath, file.RelativePath) };
    }

    private LibraryMatch Build(RecycledFile file, string titleFolder, string originalPath)
    {
        var candidates = file.Bin.Library.Kind == LibraryKind.Series
            ? EpisodeCandidates(file, titleFolder)
            : MovieCandidates(file, titleFolder);

        return new LibraryMatch
        {
            TitleFolder = titleFolder,
            OriginalPath = originalPath,
            Candidates = candidates
        };
    }

    private List<LibraryCandidate> MovieCandidates(RecycledFile file, string titleFolder)
    {
        var edition = Edition(file.FileName);
        return VideoFiles(titleFolder, recursive: false)
            .Select(path =>
            {
                var name = Path.GetFileName(path);
                if (string.Equals(name, file.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    return (Score: 3, Candidate: new LibraryCandidate(path, "Same file name"));
                }

                var otherEdition = Edition(name);
                if (string.Equals(edition, otherEdition, StringComparison.OrdinalIgnoreCase))
                {
                    return (Score: 2, Candidate: new LibraryCandidate(path,
                        edition == null ? "Same movie" : $"Same edition ({edition})"));
                }

                return (Score: 1, Candidate: new LibraryCandidate(path,
                    otherEdition == null ? "Same movie, other cut" : $"Other edition ({otherEdition})"));
            })
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Select(c => c.Candidate)
            .ToList();
    }

    private List<LibraryCandidate> EpisodeCandidates(RecycledFile file, string showFolder)
    {
        var episode = EpisodeNumber(file.FileName);
        var result = new List<LibraryCandidate>();

        foreach (var path in VideoFiles(showFolder, recursive: true))
        {
            var name = Path.GetFileName(path);
            if (string.Equals(name, file.FileName, StringComparison.OrdinalIgnoreCase))
            {
                result.Insert(0, new LibraryCandidate(path, "Same file name"));
            }
            else if (episode != null && EpisodeNumber(name) == episode)
            {
                result.Add(new LibraryCandidate(path, $"Same episode ({episode})"));
            }
        }

        return result;
    }

    /// <summary>"S01E02" from a file name, upper case, or null. A multi-episode file gives its first episode.</summary>
    public static string? EpisodeNumber(string fileName)
    {
        var match = EpisodeRegex().Match(fileName);
        return match.Success
            ? $"S{int.Parse(match.Groups[1].Value):D2}E{int.Parse(match.Groups[2].Value):D2}"
            : null;
    }

    /// <summary>The edition in "Title (YYYY) - Edition - Version [specs]", or null.</summary>
    public static string? Edition(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var match = EditionRegex().Match(name);
        if (!match.Success)
        {
            return null;
        }

        var edition = match.Groups[1].Value.Trim().TrimEnd('-').Trim();
        return edition.Length == 0 ? null : edition;
    }

    /// <summary>"Alien (1979)" or "Show Name (2019)" from the start of a file name, or null.</summary>
    public static string? TitleFromFileName(string fileName, LibraryKind kind)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        if (kind == LibraryKind.Series)
        {
            var episode = EpisodeRegex().Match(name);
            if (episode.Success && episode.Index > 0)
            {
                return name[..episode.Index].Trim().TrimEnd('-').Trim();
            }
        }

        var year = TitleYearRegex().Match(name);
        return year.Success ? year.Value.Trim() : null;
    }

    private string? PickFolder(string name, LibraryOptions library, string? exactPath)
    {
        if (!_titleFolders.TryGetValue(name, out var paths))
        {
            return null;
        }

        if (exactPath != null && paths.Contains(exactPath))
        {
            return exactPath;
        }

        return paths.FirstOrDefault(p => IsInside(p, library.Path)) ?? paths[0];
    }

    /// <summary>
    /// Indexes the movie and show folders. A library is either flat (/movies/Alien (1979))
    /// or has category folders (/movies/live-action/Alien (1979)). A top-level folder
    /// is a title when it holds video files, season folders, or ends in a year;
    /// otherwise it is a category and its children are the titles.
    /// </summary>
    private void IndexTitleFolders(string root)
    {
        foreach (var folder in SubFolders(root))
        {
            if (LooksLikeTitle(folder))
            {
                AddTitle(folder);
                continue;
            }

            foreach (var title in SubFolders(folder))
            {
                AddTitle(title);
            }
        }
    }

    private void AddTitle(string folder)
    {
        var name = Path.GetFileName(folder);
        if (!_titleFolders.TryGetValue(name, out var list))
        {
            _titleFolders[name] = list = [];
        }

        list.Add(folder);
    }

    private bool LooksLikeTitle(string folder)
    {
        if (YearSuffixRegex().IsMatch(Path.GetFileName(folder)))
        {
            return true;
        }

        try
        {
            return Directory.EnumerateFiles(folder).Any(f => _videoExtensions.Contains(Path.GetExtension(f)))
                   || Directory.EnumerateDirectories(folder).Any(d => SeasonFolderRegex().IsMatch(Path.GetFileName(d)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private IEnumerable<string> SubFolders(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder)
                .Where(f => !Path.GetFileName(f).StartsWith('.') && !_binPaths.Contains(f))
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private List<string> VideoFiles(string folder, bool recursive)
    {
        var key = (recursive ? "r|" : "f|") + folder;
        if (_filesByFolder.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var files = new List<string>();
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                IgnoreInaccessible = true,
                AttributesToSkip = 0
            };

            foreach (var path in Directory.EnumerateFiles(folder, "*", options))
            {
                if (_videoExtensions.Contains(Path.GetExtension(path))
                    && !IsInHiddenFolder(path, folder)
                    && !Path.GetFileName(path).StartsWith('.'))
                {
                    files.Add(path);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        _filesByFolder[key] = files;
        return files;
    }

    private static bool IsInHiddenFolder(string path, string root)
    {
        var relative = Path.GetRelativePath(root, Path.GetDirectoryName(path)!);
        return relative != "." && relative.Split(Path.DirectorySeparatorChar).Any(s => s.StartsWith('.'));
    }

    private static bool IsInside(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                                && !Path.IsPathRooted(relative);
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9])S(\d{1,3})[ ._-]?E(\d{1,4})", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeRegex();

    [GeneratedRegex(@"\(\d{4}\)$")]
    private static partial Regex YearSuffixRegex();

    [GeneratedRegex(@"^(Season\s*\d+|Specials|S\d{1,2})$", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonFolderRegex();

    [GeneratedRegex(@"^.+?\(\d{4}\)")]
    private static partial Regex TitleYearRegex();

    // After "(YYYY) - ", up to the first "[" (the specs) or a second " - " (the version).
    [GeneratedRegex(@"\(\d{4}\)\s+-\s+(.+?)(?:\s+-\s+|\s*\[|$)")]
    private static partial Regex EditionRegex();
}
