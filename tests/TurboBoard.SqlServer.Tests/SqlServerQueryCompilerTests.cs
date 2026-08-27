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
            QueryDefinition.CurrentVersion,
            new(sourceId, new("sales", "Order Details")),
            [new(sourceId, "Total", "OrderTotal"), new(sourceId, "Order Id", "OrderId")]));

        var compiled = new SqlServerQueryCompiler().Compile(prepared.Query!, 100);

        Assert.Equal(
            "SELECT TOP (101) [q].[Total] AS [OrderTotal], [q].[Order Id] AS [OrderId] FROM [sales].[Order Details] AS [q];",
            compiled.InspectionText);
        Assert.Equal(100, compiled.PreviewLimit);
        Assert.Equal(["OrderTotal", "OrderId"], compiled.Columns.Select(item => item.Name));
    }

    [Fact]
    public void Filters_compile_to_ordered_parameters_and_literal_contains_escapes_wildcards()
    {
        var sourceId = Guid.NewGuid();
        var schema = new DataSourceSchema(Guid.NewGuid(), DateTimeOffset.UtcNow,
        [
            new SchemaDatabaseObject(new("sales", "Orders"), DatabaseObjectKind.Table,
            [
                new SchemaColumn("OrderId", 1, NormalizedTypeCategory.Integer, "int", false, 4, 10, 0, SchemaColumnCapabilities.Select | SchemaColumnCapabilities.Filter),
                new SchemaColumn("Reference", 2, NormalizedTypeCategory.Text, "nvarchar", true, 100, null, null, SchemaColumnCapabilities.Select | SchemaColumnCapabilities.Filter),
            ]),
        ]);
        var prepared = QueryEngine.Prepare(schema, new QueryDefinition(
            QueryDefinition.CurrentVersion,
            new(sourceId, new("sales", "Orders")),
            [new(sourceId, "OrderId", "OrderId")],
            [
                new(sourceId, "OrderId", QueryFilterOperator.GreaterThan, ["10"]),
                new(sourceId, "Reference", QueryFilterOperator.Contains, ["50%_[off]'; DROP TABLE Orders;--"]),
            ]));

        var compiled = new SqlServerQueryCompiler().Compile(prepared.Query!, 100);

        Assert.Equal(
            "SELECT TOP (101) [q].[OrderId] AS [OrderId] FROM [sales].[Orders] AS [q] WHERE [q].[OrderId] > @p0 AND [q].[Reference] LIKE @p1 ESCAPE '\\';",
            compiled.InspectionText);
        Assert.Equal(["@p0", "@p1"], compiled.Parameters.Select(item => item.Name));
        Assert.Equal(10, compiled.Parameters[0].Value);
        Assert.Equal("%50\\%\\_\\[off]'; DROP TABLE Orders;--%", compiled.Parameters[1].Value);
        Assert.DoesNotContain("DROP TABLE", compiled.InspectionText, StringComparison.Ordinal);
        Assert.Equal("nvarchar", compiled.Parameters[1].ProviderType);
        Assert.Equal(100, compiled.Parameters[1].MaximumLength);
    }

    [Fact]
    public void Literal_pattern_parameter_size_includes_wildcards_and_escape_characters()
    {
        var sourceId = Guid.NewGuid();
        var schema = new DataSourceSchema(Guid.NewGuid(), DateTimeOffset.UtcNow,
        [new SchemaDatabaseObject(new("dbo", "Items"), DatabaseObjectKind.Table,
        [new SchemaColumn("Code", 1, NormalizedTypeCategory.Text, "nvarchar", false, 3, null, null, SchemaColumnCapabilities.Select | SchemaColumnCapabilities.Filter)])]);
        var prepared = QueryEngine.Prepare(schema, new(QueryDefinition.CurrentVersion, new(sourceId, new("dbo", "Items")), [new(sourceId, "Code", "Code")], [new(sourceId, "Code", QueryFilterOperator.Contains, ["%_["])]));

        var compiled = new SqlServerQueryCompiler().Compile(prepared.Query!, 10);

        var parameter = Assert.Single(compiled.Parameters);
        Assert.Equal("%\\%\\_\\[%", parameter.Value);
        Assert.Equal(((string)parameter.Value).Length, parameter.MaximumLength);
    }

    [Fact]
    public void Nested_boolean_expression_compiles_with_exact_parentheses_and_parameter_order()
    {
        var sourceId = Guid.NewGuid();
        var schema = new DataSourceSchema(Guid.NewGuid(), DateTimeOffset.UtcNow,
        [new SchemaDatabaseObject(new("dbo", "Items"), DatabaseObjectKind.Table,
        [new SchemaColumn("Id", 1, NormalizedTypeCategory.Integer, "int", false, 4, 10, 0, SchemaColumnCapabilities.Select | SchemaColumnCapabilities.Filter)])]);
        var expression = new QueryFilterGroup(Guid.NewGuid(), true, QueryFilterGroupOperator.And,
        [
            new QueryFilterCondition(Guid.NewGuid(), true, new(sourceId, "Id", QueryFilterOperator.GreaterThan, ["1"])),
            new QueryFilterNot(Guid.NewGuid(), true, new QueryFilterGroup(Guid.NewGuid(), true, QueryFilterGroupOperator.Or,
            [
                new QueryFilterCondition(Guid.NewGuid(), true, new(sourceId, "Id", QueryFilterOperator.Equal, ["2"])),
                new QueryFilterCondition(Guid.NewGuid(), true, new(sourceId, "Id", QueryFilterOperator.Equal, ["3"])),
            ])),
        ]);
        var prepared = QueryEngine.Prepare(schema, new(QueryDefinition.CurrentVersion, new(sourceId, new("dbo", "Items")), [new(sourceId, "Id", "Id")], FilterExpression: expression));

        var compiled = new SqlServerQueryCompiler().Compile(prepared.Query!, 10);

        Assert.Equal("SELECT TOP (11) [q].[Id] AS [Id] FROM [dbo].[Items] AS [q] WHERE [q].[Id] > @p0 AND NOT ([q].[Id] = @p1 OR [q].[Id] = @p2);", compiled.InspectionText);
        Assert.Equal([1, 2, 3], compiled.Parameters.Select(item => item.Value));
    }

    [Fact]
    public void Every_sql_operator_family_preserves_parameter_order_and_null_semantics()
    {
        var sourceId = Guid.NewGuid();
        var schema = new DataSourceSchema(Guid.NewGuid(), DateTimeOffset.UtcNow,
        [new SchemaDatabaseObject(new("dbo", "Items"), DatabaseObjectKind.Table,
        [new SchemaColumn("Value", 1, NormalizedTypeCategory.Text, "nvarchar", true, 50, null, null, SchemaColumnCapabilities.Select | SchemaColumnCapabilities.Filter)])]);
        var operators = new[]
        {
            QueryFilterOperator.Equal, QueryFilterOperator.NotEqual, QueryFilterOperator.LessThan,
            QueryFilterOperator.LessThanOrEqual, QueryFilterOperator.GreaterThan, QueryFilterOperator.GreaterThanOrEqual,
            QueryFilterOperator.Like, QueryFilterOperator.NotLike, QueryFilterOperator.StartsWith, QueryFilterOperator.EndsWith,
        };
        var filters = operators.Select(item => new QueryFilter(sourceId, "Value", item, [item.ToString()])).Concat(
        [
            new(sourceId, "Value", QueryFilterOperator.In, ["a", "b"]),
            new(sourceId, "Value", QueryFilterOperator.NotIn, ["c", "d"]),
            new(sourceId, "Value", QueryFilterOperator.Between, ["e", "f"]),
            new(sourceId, "Value", QueryFilterOperator.IsNull, []),
            new(sourceId, "Value", QueryFilterOperator.IsNotNull, []),
        ]).ToArray();
        var prepared = QueryEngine.Prepare(schema, new(QueryDefinition.CurrentVersion, new(sourceId, new("dbo", "Items")), [new(sourceId, "Value", "Value")], filters));

        var compiled = new SqlServerQueryCompiler().Compile(prepared.Query!, 10);

        Assert.Equal(16, compiled.Parameters.Count);
        Assert.Equal(Enumerable.Range(0, 16).Select(index => $"@p{index}"), compiled.Parameters.Select(item => item.Name));
        Assert.Contains("[q].[Value] IS NULL", compiled.InspectionText, StringComparison.Ordinal);
        Assert.Contains("[q].[Value] IS NOT NULL", compiled.InspectionText, StringComparison.Ordinal);
        Assert.Contains("[q].[Value] = @p0", compiled.InspectionText, StringComparison.Ordinal);
        Assert.Contains("[q].[Value] <> @p1", compiled.InspectionText, StringComparison.Ordinal);
        Assert.Contains("[q].[Value] < @p2", compiled.InspectionText, StringComparison.Ordinal);
        Assert.Contains("[q].[Value] <= @p3", compiled.InspectionText, StringComparison.Ordinal);
        Assert.Contains("[q].[Value] > @p4", compiled.InspectionText, StringComparison.Ordinal);
        Assert.Contains("[q].[Value] >= @p5", compiled.InspectionText, StringComparison.Ordinal);
        Assert.Contains("[q].[Value] LIKE @p6", compiled.InspectionText, StringComparison.Ordinal);
        Assert.Contains("[q].[Value] NOT LIKE @p7", compiled.InspectionText, StringComparison.Ordinal);
        Assert.Contains("[q].[Value] IN (@p10, @p11)", compiled.InspectionText, StringComparison.Ordinal);
        Assert.Contains("[q].[Value] NOT IN (@p12, @p13)", compiled.InspectionText, StringComparison.Ordinal);
        Assert.Contains("[q].[Value] BETWEEN @p14 AND @p15", compiled.InspectionText, StringComparison.Ordinal);
        Assert.DoesNotContain("= NULL", compiled.InspectionText, StringComparison.Ordinal);
    }

    [Fact]
    public void Sql_server_parameter_limit_returns_a_diagnostic()
    {
        var sourceId = Guid.NewGuid();
        var schema = new DataSourceSchema(Guid.NewGuid(), DateTimeOffset.UtcNow,
        [new SchemaDatabaseObject(new("dbo", "Items"), DatabaseObjectKind.Table,
        [new SchemaColumn("Id", 1, NormalizedTypeCategory.Integer, "int", false, 4, 10, 0, SchemaColumnCapabilities.Select | SchemaColumnCapabilities.Filter)])]);
        var values = Enumerable.Range(1, 2101).Select(item => item.ToString()).ToArray();
        var prepared = QueryEngine.Prepare(schema, new(QueryDefinition.CurrentVersion, new(sourceId, new("dbo", "Items")), [new(sourceId, "Id", "Id")], [new(sourceId, "Id", QueryFilterOperator.In, values)]));

        var exception = Assert.Throws<QueryCompilationException>(() => new SqlServerQueryCompiler().Compile(prepared.Query!, 10));

        Assert.Equal("query.filter.parameter-limit", Assert.Single(exception.Diagnostics).Code);
    }
}
