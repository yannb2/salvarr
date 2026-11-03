using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Salvarr.Arr;
using Salvarr.Library;
using Salvarr.Options;

namespace Salvarr.Tests;

[TestClass]
public sealed class ArrTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    private static BinSettings Settings(int days = 7, DateTime? next = null) =>
        new("Radarr", "/movies/.recycle", days, TimeSpan.FromDays(1), Now.AddHours(-19), next ?? Now.AddHours(5));

    [TestMethod]
    public void File_goes_at_the_first_cleanup_after_it_expires()
    {
        // Expires in 2 hours, the next run is in 5: it goes at that run.
        var soon = Retention.For(Now.AddDays(-7).AddHours(2), Settings());
        Assert.AreEqual(Now.AddDays(-7).AddHours(2).AddDays(7), soon.ExpiresUtc);
        Assert.AreEqual(Now.AddHours(5), soon.DeleteAtUtc);

        // Moved in just now: expires in 7 days, which is 6 days and 19 hours after the next run,
        // so it goes at the 8th run from now (next + 7 days).
        var fresh = Retention.For(Now, Settings());
        Assert.AreEqual(Now.AddHours(5).AddDays(7), fresh.DeleteAtUtc);
    }

    [TestMethod]
    public void Zero_days_means_kept_forever()
    {
        var info = Retention.For(Now, Settings(days: 0));
        Assert.IsTrue(info.KeptForever);
        Assert.IsNull(info.DeleteAtUtc);
    }

    [TestMethod]
    public void Unknown_schedule_still_gives_the_expiry()
    {
        var settings = Settings() with { NextCleanupUtc = null };
        var info = Retention.For(Now, settings);
        Assert.AreEqual(Now.AddDays(7), info.ExpiresUtc);
        Assert.IsNull(info.DeleteAtUtc);
    }

    [TestMethod]
    [DataRow("/movies/.recycle", "/movies/.recycle/", true)]
    [DataRow("/movies/.recycle", "/movies/.Recycle", false)]
    [DataRow("/movies/.recycle", "", false)]
    public void Same_path(string a, string b, bool expected)
    {
        Assert.AreEqual(expected, Retention.SamePath(a, b));
    }

    [TestMethod]
    public void Retention_only_for_files_in_the_apps_own_bin()
    {
        var movies = new LibraryOptions { Name = "Movies", Path = "/movies", Kind = LibraryKind.Movies };
        var statuses = new[] { new ArrStatus("Radarr", new ArrOptions(), Settings(), null) };

        RecycledFile File(string bin) => new()
        {
            Id = "x", FullPath = bin + "/A (2000)/A (2000).mkv", RelativePath = "A (2000)/A (2000).mkv",
            Bin = new RecycleBin(movies, bin), ModifiedUtc = Now
        };

        Assert.IsNotNull(ArrService.RetentionFor(File("/movies/.recycle"), statuses));
        Assert.IsNull(ArrService.RetentionFor(File("/movies/live-action/.recycle"), statuses), "a Samba bin");
    }

    [TestMethod]
    public async Task Client_reads_settings_and_tasks_and_sends_rescans()
    {
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/radarr/api/v3/config/mediamanagement" => """{"recycleBin":"/movies/.recycle","recycleBinCleanupDays":14}""",
            "/radarr/api/v3/system/task" => """[{"name":"Clean Up Recycle Bin","taskName":"CleanUpRecycleBin","interval":1440,"nextExecution":"2026-11-03T17:00:00Z"}]""",
            "/radarr/api/v3/movie" => """[{"id":7,"title":"Alien","path":"/movies/live-action/Alien (1979)"}]""",
            "/radarr/api/v3/command" => """{"id":1}""",
            _ => null
        });
        var options = new ArrOptions { Url = "http://radarr:7878/radarr", ApiKey = "secret" };
        var client = new ArrClient(new HttpClient(handler), options, "Radarr");

        var config = await client.GetMediaManagementAsync();
        var task = (await client.GetTasksAsync()).Single();
        var movie = (await client.GetItemsAsync()).Single();
        await client.RescanAsync(movie.Id);

        Assert.AreEqual("/movies/.recycle", config!.RecycleBin);
        Assert.AreEqual(14, config.RecycleBinCleanupDays);
        Assert.AreEqual(ScheduledTask.CleanUpRecycleBin, task.TaskName);
        Assert.AreEqual(1440, task.Interval);
        Assert.AreEqual(7, movie.Id);
        Assert.IsTrue(handler.Requests.All(r => r.ApiKey == "secret"));
        Assert.AreEqual("""{"name":"RescanMovie","movieId":7}""", handler.Requests.Last().Body);
    }

    [TestMethod]
    public async Task Service_reports_a_switched_off_bin()
    {
        var handler = new FakeHandler(_ => """{"recycleBin":"","recycleBinCleanupDays":7}""");
        var options = Microsoft.Extensions.Options.Options.Create(new SalvarrOptions
        {
            Radarr = new ArrOptions { Url = "http://radarr:7878", ApiKey = "k" }
        });
        var service = new ArrService(options, new FakeFactory(handler), NullLogger<ArrService>.Instance);

        var statuses = await service.GetStatusesAsync();

        var radarr = statuses.Single(s => s.AppName == "Radarr");
        Assert.IsNull(radarr.Settings);
        StringAssert.Contains(radarr.Error, "recycle bin is off");
        Assert.IsFalse(statuses.Single(s => s.AppName == "Sonarr").IsConfigured);
    }

    private sealed record SeenRequest(string Path, string? ApiKey, string? Body);

    private sealed class FakeHandler(Func<HttpRequestMessage, string?> reply) : HttpMessageHandler
    {
        public List<SeenRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new SeenRequest(request.RequestUri!.AbsolutePath,
                request.Headers.TryGetValues("X-Api-Key", out var keys) ? keys.Single() : null, body));

            var json = reply(request);
            return json == null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
