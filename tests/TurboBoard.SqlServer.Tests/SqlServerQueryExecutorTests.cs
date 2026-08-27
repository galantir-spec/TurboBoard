using System.Data;
using TurboBoard.Core.Queries;
using TurboBoard.Core.Schemas;

namespace TurboBoard.SqlServer.Tests;

public sealed class SqlServerQueryExecutorTests
{
    [Fact]
    public async Task Executor_rejects_a_plan_not_produced_by_the_sql_server_compiler()
    {
        var request = new TurboBoard.Core.DataSources.DataSourceConnectionRequest(
            "sql-server",
            TurboBoard.Core.DataSources.DataSourceConnectionMode.Advanced,
            new Dictionary<string, string?>(),
            "Server=unused",
            false);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            new SqlServerQueryExecutor().ExecuteAsync(request, new ForgedPlan()));

        Assert.Contains("not produced by the SQL Server compiler", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Live_single_source_preview_runs_only_when_an_operator_supplies_a_test_connection()
    {
        var connectionString = Environment.GetEnvironmentVariable("TURBOBOARD_SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        var configured = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
        var request = new TurboBoard.Core.DataSources.DataSourceConnectionRequest(
            "sql-server",
            TurboBoard.Core.DataSources.DataSourceConnectionMode.Advanced,
            new Dictionary<string, string?>(),
            connectionString,
            configured.TrustServerCertificate);
        var discovered = await new SqlServerSchemaDiscoverer(new SqlServerCatalogReader()).DiscoverAsync(request);
        var source = discovered.Objects!.First(item => item.Columns.Any(column => column.Capabilities.HasFlag(SchemaColumnCapabilities.Select)));
        var column = source.Columns.First(item => item.Capabilities.HasFlag(SchemaColumnCapabilities.Select));
        var sourceId = Guid.NewGuid();
        var prepared = QueryEngine.Prepare(
            new(Guid.NewGuid(), DateTimeOffset.UtcNow, discovered.Objects!, discovered.Relationships),
            new(1, new(sourceId, source.QualifiedName), [new(sourceId, column.Name, "Value0")]));
        var compiled = new SqlServerQueryCompiler().Compile(prepared.Query!, 1);

        var result = await new SqlServerQueryExecutor().ExecuteAsync(request, compiled);

        Assert.True(result.RowCount <= 1);
        Assert.Single(result.Columns);
        Assert.Equal("Value0", result.Columns[0].Name);
    }

    [Fact]
    public async Task Dynamic_result_preserves_typed_values_order_and_truncation()
    {
        var table = new DataTable();
        table.Columns.Add("OrderId", typeof(int));
        table.Columns.Add("Total", typeof(decimal));
        table.Rows.Add(1, 12.50m);
        table.Rows.Add(2, 20m);
        table.Rows.Add(3, 30m);
        await using var reader = table.CreateDataReader();
        var sourceId = Guid.NewGuid();
        var schema = new DataSourceSchema(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            [
                new SchemaDatabaseObject(
                    new("sales", "Orders"),
                    DatabaseObjectKind.Table,
                    [
                        new SchemaColumn("OrderId", 1, NormalizedTypeCategory.Integer, "int", false, 4, 10, 0, SchemaColumnCapabilities.Select),
                        new SchemaColumn("Total", 2, NormalizedTypeCategory.Decimal, "decimal", false, 9, 18, 2, SchemaColumnCapabilities.Select),
                    ]),
            ]);
        var definition = new QueryDefinition(
            QueryDefinition.CurrentVersion,
            new(sourceId, new("sales", "Orders")),
            [new(sourceId, "OrderId", "OrderId"), new(sourceId, "Total", "Total")]);
        var prepared = QueryEngine.Prepare(schema, definition);
        var query = (SqlServerCompiledQuery)new SqlServerQueryCompiler().Compile(prepared.Query!, 2);

        var result = await SqlServerQueryExecutor.MaterializeAsync(reader, query, TimeSpan.FromMilliseconds(12));

        Assert.Equal(2, result.RowCount);
        Assert.True(result.WasTruncated);
        Assert.Equal(TimeSpan.FromMilliseconds(12), result.Duration);
        Assert.Equal(["OrderId", "Total"], result.Columns.Select(item => item.Name));
        Assert.Equal("Int32", result.Columns[0].ProviderType);
        Assert.Equal(1, result.Rows[0].Values[0]);
        Assert.Equal(12.50m, result.Rows[0].Values[1]);
    }

    [Fact]
    public void Executor_materializes_parameter_specs_with_sql_server_type_metadata()
    {
        using var command = new Microsoft.Data.SqlClient.SqlCommand();
        var specs = new QueryParameterSpecification[]
        {
            new("@p0", 12.34m, "decimal", null, 18, 2),
            new("@p1", "hello", "nvarchar", 100, null, null),
        };

        SqlServerQueryExecutor.AddParameters(command, specs);

        Assert.Equal(System.Data.SqlDbType.Decimal, command.Parameters[0].SqlDbType);
        Assert.Equal((byte)18, command.Parameters[0].Precision);
        Assert.Equal((byte)2, command.Parameters[0].Scale);
        Assert.Equal(System.Data.SqlDbType.NVarChar, command.Parameters[1].SqlDbType);
        Assert.Equal(100, command.Parameters[1].Size);
    }

    private sealed class ForgedPlan : ICompiledQuery
    {
        public string InspectionText => "DROP TABLE Orders";
        public int PreviewLimit => 100;
        public IReadOnlyList<DynamicResultColumn> Columns => [];
    }
}
