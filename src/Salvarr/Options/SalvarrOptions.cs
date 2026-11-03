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

    /// <summary>Read the Dolby Vision RPU of the first 500 frames with dovi_tool (a second or two per file).</summary>
    public bool ReadDolbyVision { get; set; } = true;

    /// <summary>
    /// Restore and delete buttons. Off by default: Salvarr is read-only until this
    /// is true and the libraries are mounted read-write.
    /// </summary>
    public bool AllowChanges { get; set; }

    /// <summary>Radarr, for its recycle bin settings and to rescan a movie after a restore.</summary>
    public ArrOptions Radarr { get; set; } = new();

    /// <summary>Sonarr, the same for shows.</summary>
    public ArrOptions Sonarr { get; set; } = new();

    /// <summary>The Radarr or Sonarr settings that go with a library.</summary>
    public ArrOptions ArrFor(LibraryOptions library) => library.Kind == LibraryKind.Series ? Sonarr : Radarr;

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

public class ArrOptions
{
    /// <summary>e.g. http://radarr:7878. Empty = not used.</summary>
    public string Url { get; set; } = "";

    public string ApiKey { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(ApiKey);
}

public enum LibraryKind
{
    Movies,
    Series
}
