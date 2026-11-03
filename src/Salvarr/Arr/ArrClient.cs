using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Salvarr.Options;

namespace Salvarr.Arr;

/// <summary>The few Radarr/Sonarr API calls Salvarr needs. Both apps share the v3 API shape.</summary>
public sealed class ArrClient(HttpClient http, ArrOptions options, string appName)
{
    public string AppName => appName;

    /// <summary>Settings > Media Management: the recycle bin folder and how many days files stay in it.</summary>
    public Task<MediaManagementConfig?> GetMediaManagementAsync(CancellationToken cancellationToken = default)
    {
        return GetAsync<MediaManagementConfig>("api/v3/config/mediamanagement", cancellationToken);
    }

    /// <summary>System > Tasks: when "Clean Up Recycle Bin" last ran and runs next.</summary>
    public async Task<List<ScheduledTask>> GetTasksAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync<List<ScheduledTask>>("api/v3/system/task", cancellationToken) ?? [];
    }

    /// <summary>Every movie (Radarr) or series (Sonarr): id and folder.</summary>
    public async Task<List<ArrItem>> GetItemsAsync(CancellationToken cancellationToken = default)
    {
        var path = appName == "Sonarr" ? "api/v3/series" : "api/v3/movie";
        return await GetAsync<List<ArrItem>>(path, cancellationToken) ?? [];
    }

    /// <summary>Starts RescanMovie or RescanSeries, so the app sees a restored file.</summary>
    public async Task RescanAsync(int id, CancellationToken cancellationToken = default)
    {
        object body = appName == "Sonarr"
            ? new { name = "RescanSeries", seriesId = id }
            : new { name = "RescanMovie", movieId = id };

        using var request = Request(HttpMethod.Post, "api/v3/command");
        // Buffered, so the request has a Content-Length instead of chunked encoding.
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, path);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(new Uri(options.Url.TrimEnd('/') + "/"), path));
        request.Headers.Add("X-Api-Key", options.ApiKey);
        return request;
    }
}

public sealed class MediaManagementConfig
{
    /// <summary>Empty when the recycle bin is off: deleted files are gone for good.</summary>
    [JsonPropertyName("recycleBin")] public string? RecycleBin { get; set; }

    /// <summary>0 = never clean up automatically.</summary>
    [JsonPropertyName("recycleBinCleanupDays")] public int RecycleBinCleanupDays { get; set; }
}

public sealed class ScheduledTask
{
    public const string CleanUpRecycleBin = "CleanUpRecycleBin";

    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("taskName")] public string? TaskName { get; set; }

    /// <summary>Minutes between runs.</summary>
    [JsonPropertyName("interval")] public int Interval { get; set; }

    [JsonPropertyName("lastExecution")] public DateTime? LastExecution { get; set; }

    [JsonPropertyName("nextExecution")] public DateTime? NextExecution { get; set; }
}

public sealed class ArrItem
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("title")] public string? Title { get; set; }

    [JsonPropertyName("path")] public string? Path { get; set; }
}
