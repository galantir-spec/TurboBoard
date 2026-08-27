using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using TurboBoard.Core.DataSources;
using TurboBoard.Core.Schemas;
using TurboBoard.Persistence;
using TurboBoard.Web.DataSources;
using TurboBoard.Web.Schemas;

namespace TurboBoard.Web.Tests;

public sealed class SchemaLifecycleTests
{
    [Fact]
    public async Task Concurrent_refreshes_coalesce_and_the_latest_success_survives_a_cache_miss()
    {
        var discoverer = new RecordingSchemaDiscoverer();
        await using var host = await SchemaTestHost.CreateAsync(discoverer);
        var dataSourceId = await host.WithDataSourcesAsync(service => service.SaveAsync(
            null,
            DataSourceDraft.Structured("Warehouse", "sql.internal", "analytics", true)));

        var firstRefresh = host.WithSchemasAsync(service => service.RefreshAsync(dataSourceId));
        var secondRefresh = host.WithSchemasAsync(service => service.RefreshAsync(dataSourceId));
        var results = await Task.WhenAll(firstRefresh, secondRefresh);
        host.ClearMemoryCache();
        var persisted = await host.WithSchemasAsync(service => service.GetAsync(dataSourceId));

        Assert.Equal(1, discoverer.CallCount);
        Assert.All(results, result => Assert.Equal(SchemaRefreshStatus.Succeeded, result.Status));
        Assert.Equal("sales.Orders", Assert.Single(persisted!.Objects).QualifiedName.DisplayName);
    }

    [Fact]
    public async Task A_failed_refresh_keeps_the_last_successful_schema_available()
    {
        var discoverer = new RecordingSchemaDiscoverer();
        await using var host = await SchemaTestHost.CreateAsync(discoverer);
        var dataSourceId = await host.WithDataSourcesAsync(service => service.SaveAsync(
            null,
            DataSourceDraft.Structured("Warehouse", "sql.internal", "analytics", true)));
        _ = await host.WithSchemasAsync(service => service.RefreshAsync(dataSourceId));
        discoverer.Result = new SchemaDiscoveryResult(
            SchemaDiscoveryStatus.NetworkFailure,
            "TurboBoard could not reach SQL Server.");

        var failed = await host.WithSchemasAsync(service => service.RefreshAsync(dataSourceId));
        var retained = await host.WithSchemasAsync(service => service.GetStateAsync(dataSourceId));

        Assert.Equal(SchemaRefreshStatus.Failed, failed.Status);
        Assert.Equal(SchemaDiscoveryStatus.NetworkFailure, failed.FailureStatus);
        Assert.NotNull(retained);
        Assert.Equal(SchemaDiscoveryStatus.NetworkFailure, retained.LastRefreshFailureStatus);
        Assert.Equal("sales.Orders", Assert.Single(retained.Schema.Objects).QualifiedName.DisplayName);
    }

    [Fact]
    public async Task Changing_data_source_settings_invalidates_the_known_schema()
    {
        var discoverer = new RecordingSchemaDiscoverer();
        await using var host = await SchemaTestHost.CreateAsync(discoverer);
        var dataSourceId = await host.WithDataSourcesAsync(service => service.SaveAsync(
            null,
            DataSourceDraft.Structured("Warehouse", "sql.internal", "analytics", true)));
        _ = await host.WithSchemasAsync(service => service.RefreshAsync(dataSourceId));

        _ = await host.WithDataSourcesAsync(service => service.SaveAsync(
            dataSourceId,
            DataSourceDraft.Structured("Warehouse", "new-sql.internal", "analytics", true)));
        var invalidated = await host.WithSchemasAsync(service => service.GetAsync(dataSourceId));

        Assert.Null(invalidated);
    }

    private sealed class RecordingSchemaDiscoverer : IDataSourceSchemaDiscoverer
    {
        private int callCount;

        public string ProviderKey => "sql-server";

        public int CallCount => callCount;

        public SchemaDiscoveryResult Result { get; set; } = SchemaDiscoveryResult.Succeeded(
        [
            new SchemaDatabaseObject(
                new QualifiedDatabaseObjectName("sales", "Orders"),
                DatabaseObjectKind.Table,
                [new SchemaColumn("Id", 1, NormalizedTypeCategory.Integer, "int", false, 4, 10, 0, SchemaColumnCapabilities.Select)]),
        ]);

        public async Task<SchemaDiscoveryResult> DiscoverAsync(
            DataSourceConnectionRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref callCount);
            await Task.Delay(75, cancellationToken);
            return Result;
        }
    }

    private sealed class RecordingConnectionTester : IDataSourceConnectionTester
    {
        public string ProviderKey => "sql-server";

        public Task<DataSourceConnectionTestResult> TestAsync(
            DataSourceConnectionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(DataSourceConnectionTestResult.Succeeded());
    }

    private sealed class SchemaTestHost : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        private readonly string stateDirectory;

        private SchemaTestHost(ServiceProvider services, string stateDirectory)
        {
            this.services = services;
            this.stateDirectory = stateDirectory;
        }

        public static async Task<SchemaTestHost> CreateAsync(IDataSourceSchemaDiscoverer discoverer)
        {
            var stateDirectory = Directory.CreateTempSubdirectory("TurboBoardSchemas-").FullName;
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection()
                .SetApplicationName("TurboBoard.SchemaTests")
                .PersistKeysToFileSystem(Directory.CreateDirectory(Path.Combine(stateDirectory, "keys")));
            services.AddTurboBoardPersistence(Path.Combine(stateDirectory, "turboboard.db"));
            services.AddSingleton<IDataSourceConnectionTester, RecordingConnectionTester>();
            services.AddSingleton<IDataSourceSchemaDiscoverer>(discoverer);
            services.AddTurboBoardDataSources();
            services.AddTurboBoardSchemas();
            var provider = services.BuildServiceProvider();
            await provider.InitializeTurboBoardPersistenceAsync();
            return new SchemaTestHost(provider, stateDirectory);
        }

        public Task<T> WithDataSourcesAsync<T>(Func<IDataSourceService, Task<T>> action) =>
            WithServiceAsync(action);

        public Task<T> WithSchemasAsync<T>(Func<ISchemaService, Task<T>> action) =>
            WithServiceAsync(action);

        public void ClearMemoryCache() =>
            ((MemoryCache)services.GetRequiredService<IMemoryCache>()).Compact(1);

        private async Task<T> WithServiceAsync<TService, T>(Func<TService, Task<T>> action)
            where TService : notnull
        {
            await using var scope = services.CreateAsyncScope();
            return await action(scope.ServiceProvider.GetRequiredService<TService>());
        }

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            Directory.Delete(stateDirectory, recursive: true);
        }
    }
}
