using WeTacoo.Playground.Web.Components;
using WeTacoo.Playground.Web.Services;
using MudBlazor.Services;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options => options.DetailedErrors = true);

builder.Services.AddMudServices();
builder.Services.AddSingleton<PlaygroundState>();
builder.Services.AddScoped<ScenarioEngine>();
builder.Services.AddScoped<EntityAdminService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Auto-open browser
app.Lifetime.ApplicationStarted.Register(() =>
{
    var addresses = app.Urls;
    var url = addresses.FirstOrDefault(a => a.StartsWith("http://")) ?? "http://localhost:5100";
    try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { }
});

app.Run();
