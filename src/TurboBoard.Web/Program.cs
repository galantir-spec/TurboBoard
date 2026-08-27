using Microsoft.AspNetCore.DataProtection;
using TurboBoard.Persistence;
using TurboBoard.Web.Components;
using TurboBoard.Web.Hosting;

var builder = WebApplication.CreateBuilder(args);

var statePaths = ApplicationStatePaths.Prepare(
    builder.Configuration,
    builder.Environment.ContentRootPath);

builder.Services
    .AddDataProtection()
    .SetApplicationName("TurboBoard")
    .PersistKeysToFileSystem(new DirectoryInfo(statePaths.KeyRingDirectory));
builder.Services.AddTurboBoardPersistence(statePaths.DatabasePath);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.Services.InitializeTurboBoardPersistenceAsync();
await app.RunAsync();

public partial class Program;
