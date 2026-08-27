using Microsoft.AspNetCore.DataProtection;
using TurboBoard.Persistence;
using TurboBoard.SqlServer;
using TurboBoard.Web.Components;
using TurboBoard.Web.DataSources;
using TurboBoard.Web.Hosting;
using TurboBoard.Web.Schemas;
using TurboBoard.Web.Queries;

var builder = WebApplication.CreateBuilder(args);

var statePaths = ApplicationStatePaths.Prepare(
    builder.Configuration,
    builder.Environment.ContentRootPath);

builder.Services
    .AddDataProtection()
    .SetApplicationName("TurboBoard")
    .PersistKeysToFileSystem(new DirectoryInfo(statePaths.KeyRingDirectory));
builder.Services.AddTurboBoardPersistence(statePaths.DatabasePath);
builder.Services.AddTurboBoardSqlServer();
builder.Services.AddTurboBoardDataSources();
builder.Services.AddTurboBoardSchemas();
builder.Services.AddTurboBoardQueries();

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

await DurableApplicationState.InitializeAsync(app.Services);
await app.RunAsync();

public partial class Program;
