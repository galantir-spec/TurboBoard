using TurboBoard.Core.DataSources;
using TurboBoard.Core.Schemas;

namespace TurboBoard.SqlServer.Tests;

public sealed class SqlServerSchemaDiscovererTests
{
    [Fact]
    public async Task Catalog_rows_map_to_qualified_tables_views_and_normalized_columns()
    {
        var reader = new StubCatalogReader(
        [
            new("sales", "Orders", "TABLE", "Id", 1, "int", false, 4, 10, 0),
            new("sales", "Orders", "TABLE", "Reference", 2, "nvarchar", true, 200, 0, 0),
            new("archive", "Orders", "VIEW", "RecordedAt", 1, "datetime2", false, 8, 27, 7),
            new("geo", "Places", "TABLE", "Location", 1, "geography", true, -1, 0, 0),
        ]);
        var discoverer = new SqlServerSchemaDiscoverer(reader);

        var result = await discoverer.DiscoverAsync(CreateRequest());

        Assert.Equal(SchemaDiscoveryStatus.Succeeded, result.Status);
        Assert.Collection(
            result.Objects!,
            item =>
            {
                Assert.Equal("archive.Orders", item.QualifiedName.DisplayName);
                Assert.Equal(DatabaseObjectKind.View, item.Kind);
                Assert.Equal(NormalizedTypeCategory.DateTime, Assert.Single(item.Columns).NormalizedType);
            },
            item =>
            {
                Assert.Equal("geo.Places", item.QualifiedName.DisplayName);
                var column = Assert.Single(item.Columns);
                Assert.Equal("geography", column.ProviderType);
                Assert.Equal(NormalizedTypeCategory.Unknown, column.NormalizedType);
                Assert.Equal(SchemaColumnCapabilities.None, column.Capabilities);
            },
            item =>
            {
                Assert.Equal("sales.Orders", item.QualifiedName.DisplayName);
                Assert.Equal(DatabaseObjectKind.Table, item.Kind);
                Assert.Equal(2, item.Columns.Count);
                Assert.Equal(100, item.Columns[1].MaximumLength);
            });
    }

    [Fact]
    public async Task Unexpected_catalog_failures_are_categorized_without_exposing_provider_details()
    {
        var discoverer = new SqlServerSchemaDiscoverer(
            new ThrowingCatalogReader("Server=private-sql;Password=do-not-expose"));

        var result = await discoverer.DiscoverAsync(CreateRequest());

        Assert.Equal(SchemaDiscoveryStatus.UnexpectedFailure, result.Status);
        Assert.Equal("SQL Server could not provide Schema metadata.", result.Message);
        Assert.DoesNotContain("private-sql", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Live_catalog_discovery_runs_only_when_an_operator_supplies_a_test_connection()
    {
        var connectionString = Environment.GetEnvironmentVariable("TURBOBOARD_SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var reader = new SqlServerCatalogReader();
        var rows = await reader.ReadAsync(SqlServerConnectionSettings.CreateAdvanced(connectionString));

        Assert.NotNull(rows);
    }

    private static DataSourceConnectionRequest CreateRequest() =>
        new(
            "sql-server",
            DataSourceConnectionMode.Structured,
            new Dictionary<string, string?>
            {
                [DataSourceConnectionPropertyNames.Endpoint] = "sql.internal",
                [DataSourceConnectionPropertyNames.Catalog] = "analytics",
                [DataSourceConnectionPropertyNames.IntegratedAuthentication] = "true",
            },
            null,
            false);

    private sealed class StubCatalogReader(IReadOnlyList<SqlServerCatalogColumn> columns)
        : ISqlServerCatalogReader
    {
        public Task<IReadOnlyList<SqlServerCatalogColumn>> ReadAsync(
            SqlServerConnectionSettings settings,
            CancellationToken cancellationToken = default) => Task.FromResult(columns);
    }

    private sealed class ThrowingCatalogReader(string message) : ISqlServerCatalogReader
    {
        public Task<IReadOnlyList<SqlServerCatalogColumn>> ReadAsync(
            SqlServerConnectionSettings settings,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(message);
    }
}
