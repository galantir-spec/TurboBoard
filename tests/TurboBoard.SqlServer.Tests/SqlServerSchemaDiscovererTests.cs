using TurboBoard.Core.DataSources;
using TurboBoard.Core.Schemas;
using System.Data;

namespace TurboBoard.SqlServer.Tests;

public sealed class SqlServerSchemaDiscovererTests
{
    [Fact]
    public void Catalog_key_and_index_ordinals_accept_sql_servers_tinyint_values()
    {
        using var keys = Reader(
            ("Schema", typeof(string)), ("Object", typeof(string)), ("Name", typeof(string)),
            ("Primary", typeof(bool)), ("Ordinal", typeof(byte)), ("Column", typeof(string)),
            ["sales", "Orders", "PK_Orders", true, (byte)1, "Id"]);
        using var indexes = Reader(
            ("Schema", typeof(string)), ("Object", typeof(string)), ("Name", typeof(string)),
            ("Unique", typeof(bool)), ("Ordinal", typeof(byte)), ("Included", typeof(bool)), ("Column", typeof(string)),
            ["sales", "Orders", "IX_Orders", false, (byte)1, false, "Id"]);

        Assert.True(keys.Read());
        Assert.True(indexes.Read());

        Assert.Equal(1, SqlServerCatalogReader.MapKey(keys).Ordinal);
        Assert.Equal(1, SqlServerCatalogReader.MapIndex(indexes).KeyOrdinal);
    }

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
    public async Task Composite_keys_indexes_and_relationships_preserve_qualified_direction_and_order()
    {
        var reader = new RichCatalogReader();
        var result = await new SqlServerSchemaDiscoverer(reader).DiscoverAsync(CreateRequest());

        var orders = Assert.Single(result.Objects!, item => item.QualifiedName.DisplayName == "sales.Orders");
        Assert.True(orders.Columns[0].IsIdentity);
        Assert.True(orders.Columns[2].IsComputed);
        Assert.Equal(["TenantId", "OrderId"], Assert.Single(orders.AvailableKeys, key => key.Kind == SchemaKeyKind.Primary).Columns);
        var index = Assert.Single(orders.AvailableIndexes, item => item.Name == "IX_Orders_Customer");
        Assert.Equal(["TenantId", "CustomerId"], index.KeyColumns);
        Assert.Equal(["Total"], index.IncludedColumns);
        var relationship = Assert.Single(result.Relationships!);
        Assert.Equal("sales.Orders", relationship.FromObject.DisplayName);
        Assert.Equal(["TenantId", "CustomerId"], relationship.FromColumns);
        Assert.Equal("crm.Customers", relationship.ToObject.DisplayName);
        Assert.Equal(["TenantId", "CustomerId"], relationship.ToColumns);
    }

    [Fact]
    public async Task Live_catalog_discovery_runs_only_when_an_operator_supplies_a_test_connection()
    {
        var connectionString = Environment.GetEnvironmentVariable("TURBOBOARD_SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var configuredConnection = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
        var request = new DataSourceConnectionRequest(
            "sql-server",
            DataSourceConnectionMode.Advanced,
            new Dictionary<string, string?>(),
            connectionString,
            configuredConnection.TrustServerCertificate);
        var result = await new SqlServerSchemaDiscoverer(new SqlServerCatalogReader())
            .DiscoverAsync(request);

        Assert.Equal(SchemaDiscoveryStatus.Succeeded, result.Status);
        Assert.NotNull(result.Objects);
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

    private static DataTableReader Reader(
        (string Name, Type Type) first,
        (string Name, Type Type) second,
        (string Name, Type Type) third,
        (string Name, Type Type) fourth,
        (string Name, Type Type) fifth,
        (string Name, Type Type) sixth,
        object[] values)
    {
        var table = new DataTable();
        foreach (var column in new[] { first, second, third, fourth, fifth, sixth }) table.Columns.Add(column.Name, column.Type);
        table.Rows.Add(values);
        return table.CreateDataReader();
    }

    private static DataTableReader Reader(
        (string Name, Type Type) first,
        (string Name, Type Type) second,
        (string Name, Type Type) third,
        (string Name, Type Type) fourth,
        (string Name, Type Type) fifth,
        (string Name, Type Type) sixth,
        (string Name, Type Type) seventh,
        object[] values)
    {
        var table = new DataTable();
        foreach (var column in new[] { first, second, third, fourth, fifth, sixth, seventh }) table.Columns.Add(column.Name, column.Type);
        table.Rows.Add(values);
        return table.CreateDataReader();
    }

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


    private sealed class RichCatalogReader : ISqlServerCatalogReader
    {
        public Task<IReadOnlyList<SqlServerCatalogColumn>> ReadAsync(SqlServerConnectionSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SqlServerCatalogColumn>>([
                new("sales", "Orders", "TABLE", "TenantId", 1, "int", false, 4, 10, 0, true),
                new("sales", "Orders", "TABLE", "OrderId", 2, "int", false, 4, 10, 0),
                new("sales", "Orders", "TABLE", "Total", 3, "decimal", false, 9, 18, 2, false, true),
                new("sales", "Orders", "TABLE", "CustomerId", 4, "int", false, 4, 10, 0),
                new("crm", "Customers", "TABLE", "TenantId", 1, "int", false, 4, 10, 0),
                new("crm", "Customers", "TABLE", "CustomerId", 2, "int", false, 4, 10, 0),
            ]);

        public Task<IReadOnlyList<SqlServerCatalogKey>> ReadKeysAsync(SqlServerConnectionSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SqlServerCatalogKey>>([
                new("sales", "Orders", "PK_Orders", true, 1, "TenantId"),
                new("sales", "Orders", "PK_Orders", true, 2, "OrderId"),
            ]);

        public Task<IReadOnlyList<SqlServerCatalogIndexColumn>> ReadIndexesAsync(SqlServerConnectionSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SqlServerCatalogIndexColumn>>([
                new("sales", "Orders", "IX_Orders_Customer", false, 1, false, "TenantId"),
                new("sales", "Orders", "IX_Orders_Customer", false, 2, false, "CustomerId"),
                new("sales", "Orders", "IX_Orders_Customer", false, 0, true, "Total"),
            ]);

        public Task<IReadOnlyList<SqlServerCatalogRelationshipColumn>> ReadRelationshipsAsync(SqlServerConnectionSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SqlServerCatalogRelationshipColumn>>([
                new("FK_Orders_Customers", "sales", "Orders", 1, "TenantId", "crm", "Customers", "TenantId"),
                new("FK_Orders_Customers", "sales", "Orders", 2, "CustomerId", "crm", "Customers", "CustomerId"),
            ]);
    }
}
