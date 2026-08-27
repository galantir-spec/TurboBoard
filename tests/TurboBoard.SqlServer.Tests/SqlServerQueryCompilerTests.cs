using TurboBoard.Core.Queries;
using TurboBoard.Core.Schemas;

namespace TurboBoard.SqlServer.Tests;

public sealed class SqlServerQueryCompilerTests
{
    [Fact]
    public void Validated_query_compiles_to_quoted_bounded_select()
    {
        var sourceId = Guid.NewGuid();
        var schema = new DataSourceSchema(Guid.NewGuid(), DateTimeOffset.UtcNow,
        [
            new SchemaDatabaseObject(
                new("sales", "Order Details"),
                DatabaseObjectKind.View,
                [
                    new SchemaColumn("Order Id", 1, NormalizedTypeCategory.Integer, "int", false, 4, 10, 0, SchemaColumnCapabilities.Select),
                    new SchemaColumn("Total", 2, NormalizedTypeCategory.Decimal, "decimal", true, 9, 18, 2, SchemaColumnCapabilities.Select),
                ]),
        ]);
        var prepared = QueryEngine.Prepare(schema, new QueryDefinition(
            1,
            new(sourceId, new("sales", "Order Details")),
            [new(sourceId, "Total", "OrderTotal"), new(sourceId, "Order Id", "OrderId")]));

        var compiled = new SqlServerQueryCompiler().Compile(prepared.Query!, 100);

        Assert.Equal(
            "SELECT TOP (101) [q].[Total] AS [OrderTotal], [q].[Order Id] AS [OrderId] FROM [sales].[Order Details] AS [q];",
            compiled.InspectionText);
        Assert.Equal(100, compiled.PreviewLimit);
        Assert.Equal(["OrderTotal", "OrderId"], compiled.Columns.Select(item => item.Name));
    }
}
