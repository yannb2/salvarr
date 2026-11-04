using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Salvarr.Arr;
using Salvarr.Options;

namespace Salvarr.Tests;

[TestClass]
public sealed class LiveSettingsTests
{
    private string _folder = "";

    [TestInitialize]
    public void Setup() => _folder = Path.Combine(Path.GetTempPath(), "salvarr-live-" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    private IOptions<SalvarrOptions> Options() => Microsoft.Extensions.Options.Options.Create(new SalvarrOptions
    {
        ConfigFolder = _folder,
        Radarr = new ArrOptions { Url = "http://radarr:7878", ApiKey = "from-env" }
    });

    [TestMethod]
    public void EnvironmentUntilSavedThenTheSavedValueWins()
    {
        var options = Options();
        var live = new LiveSettings(options, NullLogger<LiveSettings>.Instance);
        Assert.AreEqual("from-env", live.Radarr.ApiKey);
        Assert.IsFalse(live.Saved(LiveSettings.RadarrKey));

        Assert.IsNull(live.Save(LiveSettings.RadarrKey, new ArrOptions { Url = "http://10.1.72.11:7878", ApiKey = "saved" }));
        Assert.AreEqual("saved", live.Radarr.ApiKey);
        Assert.IsTrue(File.Exists(Path.Combine(_folder, "settings.json")));

        // A restart reads the file again; ArrService uses it.
        var again = new LiveSettings(options, NullLogger<LiveSettings>.Instance);
        Assert.AreEqual("http://10.1.72.11:7878", again.Radarr.Url);
        var arr = new ArrService(options, null!, NullLogger<ArrService>.Instance, again);
        Assert.AreEqual("saved", arr.Radarr.ApiKey);
        Assert.IsFalse(arr.Sonarr.IsConfigured);

        Assert.IsNull(again.Reset(LiveSettings.RadarrKey));
        Assert.AreEqual("from-env", again.Radarr.ApiKey);
        Assert.IsFalse(again.Saved(LiveSettings.RadarrKey));
    }

    [TestMethod]
    public void SaySoWhenConfigCannotBeWritten()
    {
        // A file where the folder should be: nothing can be created inside it.
        File.WriteAllText(_folder, "");
        try
        {
            var live = new LiveSettings(Options(), NullLogger<LiveSettings>.Instance);
            var error = live.Save(LiveSettings.SonarrKey, new ArrOptions { Url = "http://sonarr:8989", ApiKey = "k" });
            Assert.IsNotNull(error);
            Assert.Contains("Mount a writable folder", error);
            Assert.IsFalse(live.Saved(LiveSettings.SonarrKey));
        }
        finally
        {
            File.Delete(_folder);
        }
    }
}
