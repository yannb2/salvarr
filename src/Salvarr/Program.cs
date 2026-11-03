using System.Globalization;
using Salvarr.Arr;
using Salvarr.Components;
using Salvarr.Library;
using Salvarr.Media;
using Salvarr.Options;

// Numbers and dates the same way everywhere (23.976 fps, not 23,976).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<SalvarrOptions>()
    .Bind(builder.Configuration.GetSection(SalvarrOptions.Section))
    .PostConfigure(o => o.WithDefaults());

builder.Services.AddHttpClient("arr", client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddSingleton<ArrService>();
builder.Services.AddSingleton<MediaProbe>();
builder.Services.AddSingleton<RecycleBinService>();
builder.Services.AddSingleton<FileActions>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
}

app.UseAntiforgery();
app.MapStaticAssets();
app.MapGet("/health", () => Results.Ok("ok"));

// Counts for a dashboard (Homepage's custom API widget). Read-only, no key.
app.MapGet("/api/stats", async (RecycleBinService bins) =>
    Results.Ok(RecycleStats.From(await bins.GetAsync(TimeSpan.FromMinutes(5)), DateTime.UtcNow)));
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
