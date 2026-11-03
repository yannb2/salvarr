namespace Salvarr.Options;

/// <summary>
/// Settings from appsettings.json, or from environment variables in Docker:
/// <c>Salvarr__Libraries__0__Path=/movies</c>, <c>Salvarr__RecycleFolderNames__0=.recycle</c>.
/// </summary>
public class SalvarrOptions
{
    public const string Section = "Salvarr";

    /// <summary>The library roots to look in, such as /movies and /tv.</summary>
    public List<LibraryOptions> Libraries { get; set; } = [];

    /// <summary>
    /// Folder names that are recycle bins. Any folder with one of these names
    /// inside a library root (down to <see cref="SearchDepth"/>) is listed.
    /// </summary>
    public List<string> RecycleFolderNames { get; set; } = [];

    /// <summary>
    /// How deep to look for recycle bins below each library root. 1 finds
    /// /movies/.recycle, 2 also finds /movies/live-action/.recycle.
    /// </summary>
    public int SearchDepth { get; set; } = 2;

    /// <summary>Extensions shown as video files. Anything else counts as "other files".</summary>
    public List<string> VideoExtensions { get; set; } = [];

    /// <summary>Probe the first frames of HDR10 video for HDR10+ metadata (about a second per file).</summary>
    public bool DetectHdr10Plus { get; set; } = true;

    public static readonly string[] DefaultRecycleFolderNames = [".recycle"];

    public static readonly string[] DefaultVideoExtensions =
        [".mkv", ".mp4", ".m4v", ".avi", ".ts", ".m2ts", ".mov", ".webm", ".wmv", ".mpg", ".mpeg"];

    /// <summary>Fills in the defaults for lists the configuration left empty.</summary>
    public SalvarrOptions WithDefaults()
    {
        if (RecycleFolderNames.Count == 0)
        {
            RecycleFolderNames = [..DefaultRecycleFolderNames];
        }

        if (VideoExtensions.Count == 0)
        {
            VideoExtensions = [..DefaultVideoExtensions];
        }

        return this;
    }
}

public class LibraryOptions
{
    /// <summary>Shown in the UI, e.g. "Movies".</summary>
    public string Name { get; set; } = "";

    /// <summary>Path inside the container, e.g. /movies.</summary>
    public string Path { get; set; } = "";

    /// <summary>Movies match the library file by folder; Series also by S01E02.</summary>
    public LibraryKind Kind { get; set; } = LibraryKind.Movies;
}

public enum LibraryKind
{
    Movies,
    Series
}
