using System.Text;
using Salvarr.Options;

namespace Salvarr.Library;

/// <summary>A recycle bin folder found inside a library, e.g. /movies/.recycle.</summary>
public sealed record RecycleBin(LibraryOptions Library, string Path)
{
    /// <summary>The folder that holds the bin. Samba's keeptree mirrors it inside the bin.</summary>
    public string ParentPath => System.IO.Path.GetDirectoryName(Path) ?? Library.Path;
}

/// <summary>A video file in a recycle bin.</summary>
public sealed class RecycledFile
{
    public required string Id { get; init; }
    public required string FullPath { get; init; }
    public required RecycleBin Bin { get; init; }

    /// <summary>Path inside the bin, e.g. live-action/Alien (1979)/Alien (1979) [...].mkv.</summary>
    public required string RelativePath { get; init; }

    public string FileName => Path.GetFileName(FullPath);

    /// <summary>The folders inside the bin, or "" when the file sits at the top.</summary>
    public string RelativeFolder => Path.GetDirectoryName(RelativePath) ?? "";

    public long SizeBytes { get; init; }

    /// <summary>
    /// When the file went into the bin: the later of the change time (a move
    /// sets it) and the modification time (Radarr and Sonarr set it).
    /// </summary>
    public DateTime DeletedUtc { get; init; }

    public DateTime ModifiedUtc { get; init; }

    public LibraryMatch Match { get; set; } = LibraryMatch.None;
}

/// <summary>Where a recycled file came from, and what is in the library there now.</summary>
public sealed class LibraryMatch
{
    public static readonly LibraryMatch None = new();

    /// <summary>The movie or show folder in the library, e.g. /movies/live-action/Alien (1979).</summary>
    public string? TitleFolder { get; init; }

    /// <summary>Where the file most likely was before it was deleted.</summary>
    public string? OriginalPath { get; init; }

    /// <summary>Files in the library that could have replaced it, best first.</summary>
    public IReadOnlyList<LibraryCandidate> Candidates { get; init; } = [];

    public LibraryCandidate? Best => Candidates.Count > 0 ? Candidates[0] : null;
}

public sealed record LibraryCandidate(string Path, string Reason);

/// <summary>Everything one scan of the recycle bins found.</summary>
public sealed class ScanResult
{
    public static readonly ScanResult Empty = new() { ScannedUtc = DateTime.MinValue };

    public IReadOnlyList<RecycleBin> Bins { get; init; } = [];
    public IReadOnlyList<RecycledFile> Files { get; init; } = [];

    /// <summary>Non-video files in the bins (NFO, subtitles, images).</summary>
    public int OtherFileCount { get; init; }

    public long OtherFileBytes { get; init; }

    /// <summary>Libraries that could not be read, with the reason.</summary>
    public IReadOnlyList<string> Problems { get; init; } = [];

    public DateTime ScannedUtc { get; init; }

    public TimeSpan Elapsed { get; init; }
}

/// <summary>A file path as a URL-safe id, so pages can link to /file/{id}.</summary>
public static class PathId
{
    public static string Encode(string path)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(path))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string? Decode(string id)
    {
        try
        {
            var base64 = id.Replace('-', '+').Replace('_', '/');
            base64 += (base64.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
