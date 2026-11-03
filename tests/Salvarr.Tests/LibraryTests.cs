using Salvarr.Library;
using Salvarr.Options;

namespace Salvarr.Tests;

[TestClass]
public sealed class LibraryTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "salvarr-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.Delete(_root, recursive: true);
    }

    private string Touch(string relative, int bytes = 10)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    private SalvarrOptions Options() => new SalvarrOptions
    {
        Libraries =
        [
            new LibraryOptions { Name = "Movies", Path = Path.Combine(_root, "movies"), Kind = LibraryKind.Movies },
            new LibraryOptions { Name = "TV", Path = Path.Combine(_root, "tv"), Kind = LibraryKind.Series }
        ]
    }.WithDefaults();

    private ScanResult Scan() => RecycleBinScanner.Scan(Options());

    private static RecycledFile Find(ScanResult scan, string fileName) =>
        scan.Files.Single(f => f.FileName == fileName);

    [TestMethod]
    public void Samba_bin_with_category_folders_finds_the_replacement()
    {
        Touch("movies/live-action/Alien (1979)/Alien (1979) - Director's Cut [2160p][HEVC].mkv");
        Touch("movies/live-action/Alien (1979)/Alien (1979) - Theatrical [2160p][HEVC].mkv");
        Touch("movies/.recycle/live-action/Alien (1979)/Alien (1979) - Theatrical [1080p][AVC].mkv", 500);
        Touch("movies/.recycle/live-action/Alien (1979)/movie.nfo");

        var scan = Scan();
        var file = Find(scan, "Alien (1979) - Theatrical [1080p][AVC].mkv");

        Assert.HasCount(1, scan.Bins);
        Assert.AreEqual(1, scan.OtherFileCount, "the NFO is not a video");
        Assert.AreEqual(500, file.SizeBytes);
        Assert.AreEqual(Path.Combine(_root, "movies/live-action/Alien (1979)"), file.Match.TitleFolder);
        Assert.AreEqual(Path.Combine(_root, "movies/live-action/Alien (1979)/Alien (1979) - Theatrical [1080p][AVC].mkv"),
            file.Match.OriginalPath);

        // Same edition first, the other cut second.
        Assert.HasCount(2, file.Match.Candidates);
        StringAssert.EndsWith(file.Match.Best!.Path, "Alien (1979) - Theatrical [2160p][HEVC].mkv");
        Assert.AreEqual("Same edition (Theatrical)", file.Match.Best.Reason);
    }

    [TestMethod]
    public void Radarr_bin_keeps_only_the_movie_folder()
    {
        Touch("movies/animated/Up (2009)/Up (2009) [2160p][HEVC].mkv");
        Touch("movies/.recycle/Up (2009)/Up (2009) [1080p][AVC].mkv");

        var file = Find(Scan(), "Up (2009) [1080p][AVC].mkv");

        Assert.AreEqual(Path.Combine(_root, "movies/animated/Up (2009)"), file.Match.TitleFolder);
        Assert.AreEqual("Same movie", file.Match.Best!.Reason);
    }

    [TestMethod]
    public void Same_file_name_wins()
    {
        Touch("movies/Up (2009)/Up (2009) [2160p][HEVC].mkv");
        Touch("movies/Up (2009)/Up (2009) [1080p][AVC].mkv");
        Touch("movies/.recycle/Up (2009)/Up (2009) [1080p][AVC].mkv");

        var file = Find(Scan(), "Up (2009) [1080p][AVC].mkv");

        Assert.AreEqual("Same file name", file.Match.Best!.Reason);
    }

    [TestMethod]
    public void Episode_matches_by_season_and_episode()
    {
        Touch("tv/anime/Show (2019)/Season 01/Show (2019) S01E01 [2160p] - One.mkv");
        Touch("tv/anime/Show (2019)/Season 01/Show (2019) S01E02 [2160p] - Two.mkv");
        Touch("tv/.recycle/anime/Show (2019)/Season 01/Show (2019) S01E02 [1080p] - Two.mkv");

        var file = Find(Scan(), "Show (2019) S01E02 [1080p] - Two.mkv");

        Assert.HasCount(1, file.Match.Candidates);
        Assert.AreEqual("Same episode (S01E02)", file.Match.Best!.Reason);
    }

    [TestMethod]
    public void Flat_tv_library_does_not_match_season_folders()
    {
        // /tv/Show/Season 01: "Season 01" must not be taken for a show.
        Touch("tv/Other Show/Season 01/Other Show S01E05.mkv");
        Touch("tv/.recycle/Gone Show/Season 01/Gone Show S01E05.mkv");

        var file = Find(Scan(), "Gone Show S01E05.mkv");

        Assert.IsNull(file.Match.TitleFolder);
        Assert.IsEmpty(file.Match.Candidates);
        Assert.AreEqual(Path.Combine(_root, "tv/Gone Show/Season 01/Gone Show S01E05.mkv"), file.Match.OriginalPath);
    }

    [TestMethod]
    public void File_name_finds_the_folder_when_the_bin_is_flat()
    {
        Touch("movies/live-action/Heat (1995)/Heat (1995) [2160p].mkv");
        Touch("movies/.recycle/Heat (1995) [1080p].mkv");

        var file = Find(Scan(), "Heat (1995) [1080p].mkv");

        Assert.AreEqual(Path.Combine(_root, "movies/live-action/Heat (1995)"), file.Match.TitleFolder);
    }

    [TestMethod]
    public void Bins_deeper_in_the_library_are_found()
    {
        Touch("movies/live-action/.recycle/Heat (1995)/Heat (1995) [1080p].mkv");

        var scan = Scan();

        Assert.HasCount(1, scan.Bins);
        Assert.AreEqual(Path.Combine(_root, "movies/live-action/.recycle"), scan.Bins[0].Path);
    }

    [TestMethod]
    public void Missing_library_is_reported()
    {
        var scan = Scan();

        Assert.HasCount(2, scan.Problems);
        Assert.IsEmpty(scan.Files);
    }

    [TestMethod]
    public void Deleted_time_is_not_older_than_modified_time()
    {
        var path = Touch("movies/.recycle/X (2000)/X (2000).mkv");
        File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var file = Scan().Files.Single();

        Assert.AreEqual(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), file.ModifiedUtc);
        Assert.IsTrue(file.DeletedUtc > DateTime.UtcNow.AddMinutes(-5), "the change time says it moved just now");
    }

    [TestMethod]
    [DataRow("Show (2019) S01E02 - Two.mkv", "S01E02")]
    [DataRow("show.s1e2.mkv", "S01E02")]
    [DataRow("Show S01E01E02 - Double.mkv", "S01E01")]
    [DataRow("Movie (2001).mkv", null)]
    public void Episode_numbers(string fileName, string? expected)
    {
        Assert.AreEqual(expected, LibraryMatcher.EpisodeNumber(fileName));
    }

    [TestMethod]
    [DataRow("Alien (1979) - Director's Cut [2160p][HEVC].mkv", "Director's Cut")]
    [DataRow("Alien (1979) - Director's Cut - Version 2 [2160p].mkv", "Director's Cut")]
    [DataRow("Alien (1979) [2160p][HEVC].mkv", null)]
    [DataRow("Alien (1979).mkv", null)]
    public void Editions(string fileName, string? expected)
    {
        Assert.AreEqual(expected, LibraryMatcher.Edition(fileName));
    }
}
