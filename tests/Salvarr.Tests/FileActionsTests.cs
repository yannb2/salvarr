using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Salvarr.Arr;
using Salvarr.Library;
using Salvarr.Options;

namespace Salvarr.Tests;

[TestClass]
public sealed class FileActionsTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "salvarr-actions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.Delete(_root, recursive: true);
    }

    private string Touch(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, relative);
        return path;
    }

    private SalvarrOptions Options(bool allowChanges) => new SalvarrOptions
    {
        AllowChanges = allowChanges,
        Libraries = [new LibraryOptions { Name = "Movies", Path = Path.Combine(_root, "movies") }]
    }.WithDefaults();

    private FileActions Actions(SalvarrOptions options)
    {
        var wrapped = Microsoft.Extensions.Options.Options.Create(options);
        var arr = new ArrService(wrapped, new NoHttp(), NullLogger<ArrService>.Instance);
        return new FileActions(wrapped, arr, NullLogger<FileActions>.Instance);
    }

    private RecycledFile Scanned(SalvarrOptions options, string fileName) =>
        RecycleBinScanner.Scan(options).Files.Single(f => f.FileName == fileName);

    [TestMethod]
    public async Task Restore_moves_the_file_and_its_subtitles_back_and_cleans_the_bin()
    {
        Touch("movies/live-action/Up (2009)/Up (2009) [2160p].mkv");
        Touch("movies/.recycle/live-action/Up (2009)/Up (2009) [1080p].mkv");
        Touch("movies/.recycle/live-action/Up (2009)/Up (2009) [1080p].en.srt");
        var options = Options(allowChanges: true);
        var file = Scanned(options, "Up (2009) [1080p].mkv");

        var result = await Actions(options).RestoreAsync(file, rescan: true);

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(File.Exists(Path.Combine(_root, "movies/live-action/Up (2009)/Up (2009) [1080p].mkv")));
        Assert.IsTrue(File.Exists(Path.Combine(_root, "movies/live-action/Up (2009)/Up (2009) [1080p].en.srt")));
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "movies/.recycle/live-action")), "empty folders removed");
        Assert.IsTrue(Directory.Exists(Path.Combine(_root, "movies/.recycle")), "the bin itself stays");
        StringAssert.Contains(result.Message, "Radarr is not set up");
    }

    [TestMethod]
    public async Task Restore_never_overwrites()
    {
        Touch("movies/Up (2009)/Up (2009).mkv");
        Touch("movies/.recycle/Up (2009)/Up (2009).mkv");
        var options = Options(allowChanges: true);
        var file = Scanned(options, "Up (2009).mkv");

        var result = await Actions(options).RestoreAsync(file, rescan: false);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("A file with the same name is already there.", FileActions.RestoreBlocker(file));
        Assert.IsTrue(File.Exists(file.FullPath));
    }

    [TestMethod]
    public async Task Nothing_changes_while_changes_are_off()
    {
        Touch("movies/.recycle/Up (2009)/Up (2009).mkv");
        var options = Options(allowChanges: false);
        var file = Scanned(options, "Up (2009).mkv");
        var actions = Actions(options);

        Assert.IsFalse((await actions.RestoreAsync(file, rescan: false)).Success);
        Assert.IsFalse(actions.Delete(file).Success);
        Assert.IsFalse(actions.DeleteMany([file]).Success);
        Assert.IsTrue(File.Exists(file.FullPath));
    }

    [TestMethod]
    public void Delete_removes_the_file_and_companions_only()
    {
        Touch("movies/.recycle/Up (2009)/Up (2009).mkv");
        Touch("movies/.recycle/Up (2009)/Up (2009).nfo");
        Touch("movies/.recycle/Up (2009)/Up (2009) - Extended.mkv");
        var options = Options(allowChanges: true);
        var file = Scanned(options, "Up (2009).mkv");

        var result = Actions(options).Delete(file);

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsFalse(File.Exists(file.FullPath));
        Assert.IsFalse(File.Exists(Path.Combine(_root, "movies/.recycle/Up (2009)/Up (2009).nfo")));
        Assert.IsTrue(File.Exists(Path.Combine(_root, "movies/.recycle/Up (2009)/Up (2009) - Extended.mkv")),
            "another cut is not a companion");
    }

    [TestMethod]
    public void Delete_many_counts_what_it_deleted()
    {
        Touch("movies/.recycle/A (2000)/A (2000).mkv");
        Touch("movies/.recycle/B (2001)/B (2001).mkv");
        var options = Options(allowChanges: true);
        var files = RecycleBinScanner.Scan(options).Files.ToList();

        var result = Actions(options).DeleteMany(files);

        Assert.IsTrue(result.Success);
        StringAssert.StartsWith(result.Message, "Deleted 2 files");
        Assert.IsEmpty(RecycleBinScanner.Scan(options).Files);
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("No HTTP in these tests");
    }
}
