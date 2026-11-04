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
    /// Measure the audio bit rate of lossless tracks that do not store one (no
    /// MKV statistics tags and nothing in mediainfo), on 6 samples of 10 s.
    /// </summary>
    public bool EstimateAudioBitRate { get; set; } = true;

    /// <summary>
    /// Restore and delete buttons. Off by default: Salvarr is read-only until this
    /// is true and the libraries are mounted read-write.
    /// </summary>
    public bool AllowChanges { get; set; }

    /// <summary>
    /// Where Salvarr keeps what the Settings page saves (settings.json), as the other apps keep
    /// theirs in /config. Mount /config/salvarr here.
    /// </summary>
    public string ConfigFolder { get; set; } = "/config";

    /// <summary>Radarr, for its recycle bin settings and to rescan a movie after a restore.</summary>
    public ArrOptions Radarr { get; set; } = new();

    /// <summary>Sonarr, the same for shows.</summary>
    public ArrOptions Sonarr { get; set; } = new();

    /// <summary>Looking for newer tool versions on the Tools page (never installs anything).</summary>
    public UpdateCheckOptions Updates { get; set; } = new();

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

public class UpdateCheckOptions
{
    /// <summary>Look for new tool versions on the internet once a day. Nothing is ever installed by the app.</summary>
    public bool Enabled { get; set; } = true;

    public double IntervalHours { get; set; } = 24;
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

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(ApiKey);
}

public enum LibraryKind
{
    Movies,
    Series
}
