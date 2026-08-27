using TurboBoard.Core.Queries;
using TurboBoard.Core.Schemas;
using System.Text.Json;

namespace TurboBoard.Core.Tests;

public sealed class QueryEngineTests
{
    [Fact]
    public void Unknown_columns_malicious_aliases_and_duplicate_outputs_are_safe_diagnostics()
    {
        var sourceId = Guid.NewGuid();
        var definition = new QueryDefinition(
            QueryDefinition.CurrentVersion,
            new QuerySource(sourceId, new("sales", "Orders")),
            [
                new(sourceId, "Missing; DROP TABLE Orders", "Missing"),
                new(sourceId, "OrderId", "OrderId]; DROP TABLE Orders;--"),
                new(sourceId, "OrderId", "Total"),
                new(sourceId, "OrderId", "total"),
            ]);

        var result = QueryEngine.Prepare(SchemaWithOrders(), definition);

        Assert.False(result.IsValid);
        Assert.Null(result.Query);
        Assert.Contains(result.Diagnostics, item => item.Code == "query.selection.column-unknown");
        Assert.Contains(result.Diagnostics, item => item.Code == "query.selection.alias-invalid");
        Assert.Contains(result.Diagnostics, item => item.Code == "query.selection.alias-duplicate");
    }

    [Fact]
    public void Version_one_serialization_contains_only_provider_neutral_editor_state()
    {
        var sourceId = Guid.NewGuid();
        var definition = new QueryDefinition(1, new(sourceId, new("sales", "Orders")), [new(sourceId, "OrderId", "OrderId")]);

        var json = JsonSerializer.Serialize(definition);
        var reopened = JsonSerializer.Deserialize<QueryDefinition>(json);

        Assert.NotNull(reopened);
        Assert.Equal(1, reopened.Version);
        Assert.Equal(sourceId, reopened.Source.Id);
        Assert.Equal("sales.Orders", reopened.Source.Object.DisplayName);
        Assert.Equal(new QuerySelection(sourceId, "OrderId", "OrderId"), Assert.Single(reopened.Selections));
        Assert.Contains("sales", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"CommandText\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"GeneratedSql\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ExecutableQuery\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Valid_definition_lowers_to_an_executable_query_with_schema_owned_metadata()
    {
        var sourceId = Guid.NewGuid();
        var schema = SchemaWithOrders();
        var definition = new QueryDefinition(
            QueryDefinition.CurrentVersion,
            new QuerySource(sourceId, new("sales", "Orders")),
            [new QuerySelection(sourceId, "OrderId", "OrderNumber")]);

        var result = QueryEngine.Prepare(schema, definition);

        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
        var selection = Assert.Single(result.Query!.Selections);
        Assert.Equal("OrderId", selection.Column.Name);
        Assert.Equal(NormalizedTypeCategory.Integer, selection.Column.NormalizedType);
        Assert.Equal("OrderNumber", selection.OutputName);
    }

    private static DataSourceSchema SchemaWithOrders() =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow,
        [
            new SchemaDatabaseObject(
                new("sales", "Orders"),
                DatabaseObjectKind.Table,
                [new SchemaColumn("OrderId", 1, NormalizedTypeCategory.Integer, "int", false, 4, 10, 0, SchemaColumnCapabilities.Select)]),
        ]);
}
