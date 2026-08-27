using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TurboBoard.Core.DataSources;
using TurboBoard.Core.Schemas;
using TurboBoard.Web.DataSources;
using TurboBoard.Web.Schemas;
using TurboBoard.Core.Queries;
using TurboBoard.Web.Queries;

namespace TurboBoard.Web.Tests;

public sealed class ApplicationShellTests
{
    [Fact]
    public async Task Analyst_can_open_the_shell_and_primary_destinations()
    {
        using var stateDirectory = TemporaryDirectory.Create();
        await using var application = new TurboBoardApplicationFactory(stateDirectory.Path);
        using var client = application.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        var overview = await client.GetAsync("/");
        var overviewHtml = await overview.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        Assert.Contains("TurboBoard", overviewHtml, StringComparison.Ordinal);
        Assert.Contains("href=\"data-sources\"", overviewHtml, StringComparison.Ordinal);
        Assert.Contains("href=\"queries\"", overviewHtml, StringComparison.Ordinal);

        var dataSources = await client.GetAsync("/data-sources");
        var dataSourcesHtml = await dataSources.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, dataSources.StatusCode);
        Assert.Contains("Data Sources", dataSourcesHtml, StringComparison.Ordinal);
        Assert.Contains("Add Data Source", dataSourcesHtml, StringComparison.Ordinal);
        Assert.Contains("Encrypted transport and certificate validation are enabled by default", dataSourcesHtml, StringComparison.Ordinal);

        var schemaExplorer = await client.GetAsync($"/data-sources/{Guid.NewGuid()}/schema");
        var schemaExplorerHtml = await schemaExplorer.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, schemaExplorer.StatusCode);
        Assert.Contains("Schema Explorer", schemaExplorerHtml, StringComparison.Ordinal);
        Assert.Contains("Data Source not found", schemaExplorerHtml, StringComparison.Ordinal);

        var queries = await client.GetAsync("/queries");
        var queriesHtml = await queries.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, queries.StatusCode);
        Assert.Contains("Queries", queriesHtml, StringComparison.Ordinal);
        Assert.Contains("Run preview", queriesHtml, StringComparison.Ordinal);
        Assert.Contains("Choose a Data Source", queriesHtml, StringComparison.Ordinal);
        Assert.Contains("Save As", queriesHtml, StringComparison.Ordinal);
        Assert.Contains("Reset", queriesHtml, StringComparison.Ordinal);
        Assert.Contains("Duplicate", queriesHtml, StringComparison.Ordinal);
        Assert.Contains("Delete", queriesHtml, StringComparison.Ordinal);
        Assert.Contains("unsaved changes", queriesHtml, StringComparison.OrdinalIgnoreCase);

        Assert.True(File.Exists(Path.Combine(stateDirectory.Path, "turboboard.db")));
        Assert.True(Directory.Exists(Path.Combine(stateDirectory.Path, "keys")));
    }

    [Fact]
    public async Task Startup_materializes_a_durable_data_protection_key()
    {
        using var stateDirectory = TemporaryDirectory.Create();
        await using var application = new TurboBoardApplicationFactory(stateDirectory.Path);
        using var client = application.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        Assert.NotEmpty(Directory.EnumerateFiles(
            Path.Combine(stateDirectory.Path, "keys"),
            "*.xml",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task Startup_wraps_database_initialization_failures_safely()
    {
        using var stateDirectory = TemporaryDirectory.Create();
        _ = Directory.CreateDirectory(Path.Combine(stateDirectory.Path, "turboboard.db"));
        await using var application = new TurboBoardApplicationFactory(stateDirectory.Path);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            application.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            }));

        Assert.StartsWith(
            "TurboBoard durable state could not be initialized",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_fails_safely_when_the_state_location_is_not_a_directory()
    {
        using var testDirectory = TemporaryDirectory.Create();
        var invalidStatePath = Path.Combine(testDirectory.Path, "state-file");
        await File.WriteAllTextAsync(invalidStatePath, "not a directory");
        await using var application = new TurboBoardApplicationFactory(invalidStatePath);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            application.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            }));

        Assert.Contains(
            "TurboBoard durable state could not be initialized",
            exception.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain("not a directory", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Schema_explorer_renders_runtime_discovered_metadata()
    {
        using var stateDirectory = TemporaryDirectory.Create();
        await using var application = new TurboBoardApplicationFactory(
            stateDirectory.Path,
            services =>
            {
                services.RemoveAll<IDataSourceSchemaDiscoverer>();
                services.AddSingleton<IDataSourceSchemaDiscoverer, ExplorerSchemaDiscoverer>();
            });
        using var client = application.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
        await using var scope = application.Services.CreateAsyncScope();
        var dataSources = scope.ServiceProvider.GetRequiredService<IDataSourceService>();
        var schemas = scope.ServiceProvider.GetRequiredService<ISchemaService>();
        var dataSourceId = await dataSources.SaveAsync(
            null,
            DataSourceDraft.Structured("Warehouse", "sql.internal", "analytics", true));
        _ = await schemas.RefreshAsync(dataSourceId);

        var response = await client.GetAsync($"/data-sources/{dataSourceId}/schema");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("sales", html, StringComparison.Ordinal);
        Assert.Contains("Orders", html, StringComparison.Ordinal);
        Assert.Contains("Table", html, StringComparison.Ordinal);
        Assert.Contains("Id", html, StringComparison.Ordinal);
        Assert.Contains("Integer", html, StringComparison.Ordinal);
        Assert.Contains("int", html, StringComparison.Ordinal);
        Assert.Contains("No", html, StringComparison.Ordinal);
        Assert.Contains("Search Schema", html, StringComparison.Ordinal);
        Assert.Contains("Identity", html, StringComparison.Ordinal);
        Assert.Contains("PK", html, StringComparison.Ordinal);
        Assert.Contains("Relationships", html, StringComparison.Ordinal);
        Assert.Contains("sales.Orders", html, StringComparison.Ordinal);
        Assert.Contains("crm.Customers", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_preview_service_validates_compiles_executes_and_returns_dynamic_results()
    {
        using var stateDirectory = TemporaryDirectory.Create();
        await using var application = new TurboBoardApplicationFactory(
            stateDirectory.Path,
            services =>
            {
                services.RemoveAll<IDataSourceSchemaDiscoverer>();
                services.AddSingleton<IDataSourceSchemaDiscoverer, ExplorerSchemaDiscoverer>();
                services.RemoveAll<IQueryExecutor>();
                services.AddSingleton<IQueryExecutor, RecordingQueryExecutor>();
            });
        await using var scope = application.Services.CreateAsyncScope();
        var dataSources = scope.ServiceProvider.GetRequiredService<IDataSourceService>();
        var schemas = scope.ServiceProvider.GetRequiredService<ISchemaService>();
        var previews = scope.ServiceProvider.GetRequiredService<IQueryPreviewService>();
        var dataSourceId = await dataSources.SaveAsync(null, DataSourceDraft.Structured("Warehouse", "sql.internal", "analytics", true));
        _ = await schemas.RefreshAsync(dataSourceId);
        var sourceId = Guid.NewGuid();

        var preview = await previews.PreviewAsync(dataSourceId, new QueryDefinition(
            QueryDefinition.CurrentVersion,
            new(sourceId, new("sales", "Orders")),
            [new(sourceId, "Id", "OrderId")]));

        Assert.Equal(QueryPreviewStatus.Succeeded, preview.Status);
        Assert.Contains("SELECT TOP (101)", preview.GeneratedSql, StringComparison.Ordinal);
        Assert.Equal(42, Assert.Single(preview.Result!.Rows).Values[0]);
    }

    private sealed class TurboBoardApplicationFactory(
        string stateDirectory,
        Action<IServiceCollection>? configureServices = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("TurboBoard:StateDirectory", stateDirectory);
            if (configureServices is not null)
            {
                builder.ConfigureTestServices(configureServices);
            }
        }
    }

    private sealed class ExplorerSchemaDiscoverer : IDataSourceSchemaDiscoverer
    {
        public string ProviderKey => "sql-server";

        public Task<SchemaDiscoveryResult> DiscoverAsync(
            DataSourceConnectionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SchemaDiscoveryResult.Succeeded(
            [
                new SchemaDatabaseObject(
                    new QualifiedDatabaseObjectName("sales", "Orders"),
                    DatabaseObjectKind.Table,
                    [new SchemaColumn("Id", 1, NormalizedTypeCategory.Integer, "int", false, 4, 10, 0, SchemaColumnCapabilities.Select, true)],
                    [new SchemaKey("PK_Orders", SchemaKeyKind.Primary, ["Id"])]),
                new SchemaDatabaseObject(
                    new QualifiedDatabaseObjectName("crm", "Customers"),
                    DatabaseObjectKind.Table,
                    [new SchemaColumn("Id", 1, NormalizedTypeCategory.Integer, "int", false, 4, 10, 0, SchemaColumnCapabilities.Select)]),
            ],
            [new SchemaRelationship("FK_Orders_Customers", new("sales", "Orders"), ["Id"], new("crm", "Customers"), ["Id"])]));
    }

    private sealed class RecordingQueryExecutor : IQueryExecutor
    {
        public string ProviderKey => "sql-server";

        public Task<DynamicResult> ExecuteAsync(DataSourceConnectionRequest connection, ICompiledQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DynamicResult(query.Columns, [new DynamicResultRow([42])], TimeSpan.FromMilliseconds(5), false));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            return new TemporaryDirectory(
                Directory.CreateTempSubdirectory("TurboBoardTests-").FullName);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
