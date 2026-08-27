using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TurboBoard.Core.Queries;
using TurboBoard.Core.Schemas;
using TurboBoard.Persistence;
using TurboBoard.Web.Queries;

namespace TurboBoard.Web.Tests;

public sealed class SavedQueryLifecycleTests
{
    [Fact]
    public async Task Saved_query_reopens_after_a_service_restart()
    {
        using var database = TemporaryDatabase.Create();
        var dataSourceId = Guid.NewGuid();
        var definition = Definition("Orders", "Id");
        Guid savedQueryId;

        await using (var host = await SavedQueryTestHost.CreateAsync(database.Path))
        {
            await host.AddDataSourceAsync(dataSourceId);
            savedQueryId = await host.WithServiceAsync(service => service.SaveAsync(
                null,
                new(dataSourceId, "Orders by id", "Reusable order lookup", definition)));
        }

        await using var restarted = await SavedQueryTestHost.CreateAsync(database.Path);
        var reopened = await restarted.WithServiceAsync(service => service.GetAsync(savedQueryId));

        Assert.NotNull(reopened);
        Assert.Equal("Orders by id", reopened.Name);
        Assert.Equal(definition.Version, reopened.Definition?.Version);
        Assert.Equal(definition.Source, reopened.Definition?.Source);
        Assert.Equal(definition.Selections, reopened.Definition?.Selections);
        Assert.Null(reopened.Diagnostic);
        Assert.DoesNotContain("sql", await restarted.ReadDefinitionJsonAsync(savedQueryId), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Duplicate_is_distinct_and_delete_removes_only_the_selected_saved_query()
    {
        using var database = TemporaryDatabase.Create();
        await using var host = await SavedQueryTestHost.CreateAsync(database.Path);
        var dataSourceId = Guid.NewGuid();
        await host.AddDataSourceAsync(dataSourceId);
        var originalId = await host.WithServiceAsync(service => service.SaveAsync(
            null,
            new(dataSourceId, "Orders", "", Definition("Orders", "Id"))));

        var duplicateId = await host.WithServiceAsync(service => service.DuplicateAsync(originalId, "Orders copy"));
        var deleted = await host.WithServiceAsync(service => service.DeleteAsync(originalId));
        var remaining = await host.WithServiceAsync(service => service.ListAsync(dataSourceId));

        Assert.NotEqual(originalId, duplicateId);
        Assert.True(deleted);
        Assert.Equal(duplicateId, Assert.Single(remaining).Id);
        Assert.Equal("Orders copy", remaining[0].Name);
    }

    [Fact]
    public async Task Saving_an_existing_query_updates_it_while_save_as_creates_a_distinct_query()
    {
        using var database = TemporaryDatabase.Create();
        await using var host = await SavedQueryTestHost.CreateAsync(database.Path);
        var dataSourceId = Guid.NewGuid();
        await host.AddDataSourceAsync(dataSourceId);
        var originalId = await host.WithServiceAsync(service => service.SaveAsync(
            null,
            new(dataSourceId, "Orders", "", Definition("Orders", "Id"))));

        var updatedId = await host.WithServiceAsync(service => service.SaveAsync(
            originalId,
            new(dataSourceId, "Renamed", "Updated", Definition("Orders", "Amount"))));
        var saveAsId = await host.WithServiceAsync(service => service.SaveAsync(
            null,
            new(dataSourceId, "Renamed copy", "Updated", Definition("Orders", "Amount"))));

        Assert.Equal(originalId, updatedId);
        Assert.NotEqual(originalId, saveAsId);
        Assert.Equal(2, (await host.WithServiceAsync(service => service.ListAsync(dataSourceId))).Count);
    }

    [Fact]
    public async Task Unknown_newer_definition_version_returns_an_editable_safe_diagnostic()
    {
        using var database = TemporaryDatabase.Create();
        await using var host = await SavedQueryTestHost.CreateAsync(database.Path);
        var dataSourceId = Guid.NewGuid();
        var id = Guid.NewGuid();
        await host.AddDataSourceAsync(dataSourceId);
        await host.InsertSavedQueryAsync(new SavedQueryRecord
        {
            Id = id,
            DataSourceId = dataSourceId,
            Name = "From the future",
            Description = "Can still edit metadata",
            DefinitionJson = "{\"version\":99,\"futureShape\":{}}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

        var result = await host.WithServiceAsync(service => service.GetAsync(id));

        Assert.NotNull(result);
        Assert.Null(result.Definition);
        Assert.Equal("query.definition.version.unsupported", result.Diagnostic?.Code);
        Assert.Equal("From the future", result.Name);
        Assert.Contains("newer version", result.Diagnostic?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Version_boundary_rejects_unknown_newer_definitions_without_throwing()
    {
        using var database = TemporaryDatabase.Create();
        await using var host = await SavedQueryTestHost.CreateAsync(database.Path);

        var result = host.Deserialize("{\"version\":2147483647,\"futureShape\":{}}");

        Assert.Null(result.Definition);
        Assert.Equal("query.definition.version.unsupported", result.Diagnostic?.Code);
    }

    [Fact]
    public async Task Unsupported_definition_metadata_can_be_edited_without_rewriting_its_json()
    {
        using var database = TemporaryDatabase.Create();
        await using var host = await SavedQueryTestHost.CreateAsync(database.Path);
        var dataSourceId = Guid.NewGuid();
        var id = Guid.NewGuid();
        const string futureJson = "{\"version\":99,\"futureShape\":{\"keep\":true}}";
        await host.AddDataSourceAsync(dataSourceId);
        await host.InsertSavedQueryAsync(new SavedQueryRecord
        {
            Id = id,
            DataSourceId = dataSourceId,
            Name = "Future",
            Description = "",
            DefinitionJson = futureJson,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

        await host.WithServiceAsync(service => service.UpdateMetadataAsync(id, "Future renamed", "Still safe"));
        var reopened = await host.WithServiceAsync(service => service.GetAsync(id));

        Assert.Equal("Future renamed", reopened?.Name);
        Assert.Equal("Still safe", reopened?.Description);
        Assert.Equal(futureJson, await host.ReadDefinitionJsonAsync(id));
    }

    private static QueryDefinition Definition(string objectName, string columnName)
    {
        var sourceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        return new(
            QueryDefinition.CurrentVersion,
            new(sourceId, new QualifiedDatabaseObjectName("sales", objectName)),
            [new(sourceId, columnName, columnName)]);
    }

    private sealed class SavedQueryTestHost(ServiceProvider services) : IAsyncDisposable
    {
        public static async Task<SavedQueryTestHost> CreateAsync(string databasePath)
        {
            var services = new ServiceCollection()
                .AddLogging()
                .AddTurboBoardPersistence(databasePath)
                .AddTurboBoardQueries()
                .BuildServiceProvider();
            await services.InitializeTurboBoardPersistenceAsync();
            return new(services);
        }

        public async Task<T> WithServiceAsync<T>(Func<ISavedQueryService, Task<T>> action)
        {
            await using var scope = services.CreateAsyncScope();
            return await action(scope.ServiceProvider.GetRequiredService<ISavedQueryService>());
        }

        public async Task WithServiceAsync(Func<ISavedQueryService, Task> action)
        {
            await using var scope = services.CreateAsyncScope();
            await action(scope.ServiceProvider.GetRequiredService<ISavedQueryService>());
        }

        public async Task AddDataSourceAsync(Guid id)
        {
            await using var scope = services.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TurboBoardDbContext>>();
            await using var context = await factory.CreateDbContextAsync();
            context.DataSources.Add(new DataSourceRecord
            {
                Id = id,
                Name = "Warehouse",
                Description = "",
                Provider = "sql-server",
                ProtectedSettings = "test",
                ConfigurationVersion = Guid.NewGuid(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            });
            await context.SaveChangesAsync();
        }

        public async Task InsertSavedQueryAsync(SavedQueryRecord record)
        {
            await using var scope = services.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TurboBoardDbContext>>();
            await using var context = await factory.CreateDbContextAsync();
            context.SavedQueries.Add(record);
            await context.SaveChangesAsync();
        }

        public QueryDefinitionReadResult Deserialize(string json) =>
            services.GetRequiredService<IQueryDefinitionSerializer>().Deserialize(json);

        public async Task<string> ReadDefinitionJsonAsync(Guid id)
        {
            await using var scope = services.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TurboBoardDbContext>>();
            await using var context = await factory.CreateDbContextAsync();
            return (await context.SavedQueries.SingleAsync(item => item.Id == id)).DefinitionJson;
        }

        public ValueTask DisposeAsync() => services.DisposeAsync();
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private TemporaryDatabase(string path) => Path = path;
        public string Path { get; }
        public static TemporaryDatabase Create() => new(System.IO.Path.Combine(
            Directory.CreateTempSubdirectory("TurboBoardSavedQueries-").FullName,
            "state.db"));
        public void Dispose()
        {
            var directory = System.IO.Path.GetDirectoryName(Path)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
