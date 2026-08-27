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

    [Fact]
    public void Integer_equality_filter_lowers_to_a_typed_executable_filter()
    {
        var sourceId = Guid.NewGuid();
        var definition = new QueryDefinition(
            QueryDefinition.CurrentVersion,
            new(sourceId, new("sales", "Orders")),
            [new(sourceId, "OrderId", "OrderId")],
            [new(sourceId, "OrderId", QueryFilterOperator.Equal, ["42"])]);

        var result = QueryEngine.Prepare(SchemaWithOrders(), definition);

        Assert.True(result.IsValid);
        var filter = Assert.Single(result.Query!.Filters);
        Assert.Equal(QueryFilterOperator.Equal, filter.Operator);
        Assert.Equal(42, Assert.Single(filter.Values));
        Assert.Equal("int", filter.Column.ProviderType);
    }

    [Fact]
    public void Invalid_filter_shapes_return_actionable_diagnostics()
    {
        var sourceId = Guid.NewGuid();
        var schema = SchemaWithOrders();
        var definition = new QueryDefinition(QueryDefinition.CurrentVersion, new(sourceId, new("sales", "Orders")), [new(sourceId, "OrderId", "OrderId")],
        [
            new(sourceId, "Missing", QueryFilterOperator.Equal, ["1"]),
            new(sourceId, "OrderId", QueryFilterOperator.Like, ["1"]),
            new(sourceId, "OrderId", QueryFilterOperator.Equal, [null]),
            new(sourceId, "OrderId", QueryFilterOperator.In, []),
            new(sourceId, "OrderId", QueryFilterOperator.Between, ["1", "not-an-integer"]),
        ]);

        var result = QueryEngine.Prepare(schema, definition);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, item => item.Code == "query.filter.column-unknown");
        Assert.Contains(result.Diagnostics, item => item.Code == "query.filter.operator-incompatible");
        Assert.Contains(result.Diagnostics, item => item.Code == "query.filter.null-comparison");
        Assert.Contains(result.Diagnostics, item => item.Code == "query.filter.values-required");
        Assert.Contains(result.Diagnostics, item => item.Code == "query.filter.value-incompatible");
    }

    [Fact]
    public void Operator_catalog_is_type_aware()
    {
        var text = QueryEngine.OperatorsFor(NormalizedTypeCategory.Text);
        var boolean = QueryEngine.OperatorsFor(NormalizedTypeCategory.Boolean);

        Assert.Contains(QueryFilterOperator.Contains, text);
        Assert.Contains(QueryFilterOperator.Between, text);
        Assert.DoesNotContain(QueryFilterOperator.Contains, boolean);
        Assert.DoesNotContain(QueryFilterOperator.Between, boolean);
        Assert.Contains(QueryFilterOperator.IsNull, boolean);
    }

    [Theory]
    [InlineData("tinyint", "-1")]
    [InlineData("tinyint", "256")]
    [InlineData("smallint", "32768")]
    [InlineData("int", "2147483648")]
    public void Provider_range_incompatible_integer_values_are_diagnostics(string providerType, string value)
    {
        var sourceId = Guid.NewGuid();
        var schema = new DataSourceSchema(Guid.NewGuid(), DateTimeOffset.UtcNow,
        [new SchemaDatabaseObject(new("dbo", "Values"), DatabaseObjectKind.Table,
        [new SchemaColumn("Number", 1, NormalizedTypeCategory.Integer, providerType, false, 8, 19, 0, SchemaColumnCapabilities.Select | SchemaColumnCapabilities.Filter)])]);
        var definition = new QueryDefinition(QueryDefinition.CurrentVersion, new(sourceId, new("dbo", "Values")), [new(sourceId, "Number", "Number")], [new(sourceId, "Number", QueryFilterOperator.Equal, [value])]);

        var result = QueryEngine.Prepare(schema, definition);

        Assert.Contains(result.Diagnostics, item => item.Code == "query.filter.value-incompatible");
    }

    [Fact]
    public void Provider_length_precision_and_scale_violations_are_diagnostics()
    {
        var sourceId = Guid.NewGuid();
        var schema = new DataSourceSchema(Guid.NewGuid(), DateTimeOffset.UtcNow,
        [new SchemaDatabaseObject(new("dbo", "Values"), DatabaseObjectKind.Table,
        [
            new SchemaColumn("Label", 1, NormalizedTypeCategory.Text, "nvarchar", true, 3, null, null, SchemaColumnCapabilities.Select | SchemaColumnCapabilities.Filter),
            new SchemaColumn("Amount", 2, NormalizedTypeCategory.Decimal, "decimal", false, 9, 5, 2, SchemaColumnCapabilities.Select | SchemaColumnCapabilities.Filter),
        ])]);
        var definition = new QueryDefinition(QueryDefinition.CurrentVersion, new(sourceId, new("dbo", "Values")), [new(sourceId, "Label", "Label")],
        [
            new(sourceId, "Label", QueryFilterOperator.Equal, ["four"]),
            new(sourceId, "Amount", QueryFilterOperator.Equal, ["1234.567"]),
        ]);

        var result = QueryEngine.Prepare(schema, definition);

        Assert.Equal(2, result.Diagnostics.Count(item => item.Code == "query.filter.value-incompatible"));
    }

    [Fact]
    public void Decimal_precision_does_not_count_the_placeholder_zero_below_one()
    {
        var sourceId = Guid.NewGuid();
        var schema = new DataSourceSchema(Guid.NewGuid(), DateTimeOffset.UtcNow,
        [new SchemaDatabaseObject(new("dbo", "Values"), DatabaseObjectKind.Table,
        [new SchemaColumn("Ratio", 1, NormalizedTypeCategory.Decimal, "decimal", false, 5, 2, 2, SchemaColumnCapabilities.Select | SchemaColumnCapabilities.Filter)])]);
        var definition = new QueryDefinition(QueryDefinition.CurrentVersion, new(sourceId, new("dbo", "Values")), [new(sourceId, "Ratio", "Ratio")], [new(sourceId, "Ratio", QueryFilterOperator.Equal, ["0.12"])]);

        var result = QueryEngine.Prepare(schema, definition);

        Assert.True(result.IsValid);
        Assert.Equal(0.12m, Assert.Single(Assert.Single(result.Query!.Filters).Values));
    }

    [Theory]
    [InlineData("datetime", "1752-12-31T23:59:59")]
    [InlineData("datetime", "9999-12-31T23:59:59.998")]
    [InlineData("smalldatetime", "2080-01-01T00:00:00")]
    [InlineData("smalldatetime", "2079-06-06T23:59:30")]
    [InlineData("time", "25:00:00")]
    public void Provider_incompatible_temporal_values_are_diagnostics(string providerType, string value)
    {
        var sourceId = Guid.NewGuid();
        var normalizedType = providerType == "time" ? NormalizedTypeCategory.Time : NormalizedTypeCategory.DateTime;
        var schema = new DataSourceSchema(Guid.NewGuid(), DateTimeOffset.UtcNow,
        [new SchemaDatabaseObject(new("dbo", "Events"), DatabaseObjectKind.Table,
        [new SchemaColumn("OccurredAt", 1, normalizedType, providerType, false, null, null, null, SchemaColumnCapabilities.Select | SchemaColumnCapabilities.Filter)])]);
        var definition = new QueryDefinition(QueryDefinition.CurrentVersion, new(sourceId, new("dbo", "Events")), [new(sourceId, "OccurredAt", "OccurredAt")], [new(sourceId, "OccurredAt", QueryFilterOperator.Equal, [value])]);

        var result = QueryEngine.Prepare(schema, definition);

        Assert.Contains(result.Diagnostics, item => item.Code == "query.filter.value-incompatible");
    }

    [Theory]
    [InlineData("datetime", typeof(DateTime), "9999-12-31T23:59:59.997")]
    [InlineData("smalldatetime", typeof(DateTime), "2079-06-06T23:59:29")]
    [InlineData("datetime2", typeof(DateTime), "2026-08-27T12:30:00+02:00")]
    [InlineData("datetimeoffset", typeof(DateTimeOffset), "2026-08-27T12:30:00+02:00")]
    public void Date_time_values_lower_to_the_clr_type_required_by_sql_server(string providerType, Type expectedType, string value)
    {
        var sourceId = Guid.NewGuid();
        var schema = new DataSourceSchema(Guid.NewGuid(), DateTimeOffset.UtcNow,
        [new SchemaDatabaseObject(new("dbo", "Events"), DatabaseObjectKind.Table,
        [new SchemaColumn("OccurredAt", 1, NormalizedTypeCategory.DateTime, providerType, false, null, null, null, SchemaColumnCapabilities.Select | SchemaColumnCapabilities.Filter)])]);
        var definition = new QueryDefinition(QueryDefinition.CurrentVersion, new(sourceId, new("dbo", "Events")), [new(sourceId, "OccurredAt", "OccurredAt")], [new(sourceId, "OccurredAt", QueryFilterOperator.Equal, [value])]);

        var result = QueryEngine.Prepare(schema, definition);

        Assert.True(result.IsValid);
        Assert.IsType(expectedType, Assert.Single(Assert.Single(result.Query!.Filters).Values));
    }

    [Theory]
    [InlineData("float", 53, "NaN")]
    [InlineData("float", 53, "Infinity")]
    [InlineData("real", 24, "1e40")]
    [InlineData("float", 24, "1e40")]
    public void Provider_incompatible_floating_point_values_are_diagnostics(string providerType, byte precision, string value)
    {
        var sourceId = Guid.NewGuid();
        var schema = new DataSourceSchema(Guid.NewGuid(), DateTimeOffset.UtcNow,
        [new SchemaDatabaseObject(new("dbo", "Values"), DatabaseObjectKind.Table,
        [new SchemaColumn("Measurement", 1, NormalizedTypeCategory.FloatingPoint, providerType, false, 8, precision, null, SchemaColumnCapabilities.Select | SchemaColumnCapabilities.Filter)])]);
        var definition = new QueryDefinition(QueryDefinition.CurrentVersion, new(sourceId, new("dbo", "Values")), [new(sourceId, "Measurement", "Measurement")], [new(sourceId, "Measurement", QueryFilterOperator.Equal, [value])]);

        var result = QueryEngine.Prepare(schema, definition);

        Assert.Contains(result.Diagnostics, item => item.Code == "query.filter.value-incompatible");
    }

    [Theory]
    [InlineData("real", 24, typeof(float))]
    [InlineData("float", 24, typeof(float))]
    [InlineData("float", 53, typeof(double))]
    public void Floating_point_values_lower_to_the_provider_clr_type(string providerType, byte precision, Type expectedType)
    {
        var sourceId = Guid.NewGuid();
        var schema = new DataSourceSchema(Guid.NewGuid(), DateTimeOffset.UtcNow,
        [new SchemaDatabaseObject(new("dbo", "Values"), DatabaseObjectKind.Table,
        [new SchemaColumn("Measurement", 1, NormalizedTypeCategory.FloatingPoint, providerType, false, 8, precision, null, SchemaColumnCapabilities.Select | SchemaColumnCapabilities.Filter)])]);
        var definition = new QueryDefinition(QueryDefinition.CurrentVersion, new(sourceId, new("dbo", "Values")), [new(sourceId, "Measurement", "Measurement")], [new(sourceId, "Measurement", QueryFilterOperator.Equal, ["12.5"])]);

        var result = QueryEngine.Prepare(schema, definition);

        Assert.True(result.IsValid);
        Assert.IsType(expectedType, Assert.Single(Assert.Single(result.Query!.Filters).Values));
    }

    [Theory]
    [InlineData("smallmoney", 10, 4, "214748.3648")]
    [InlineData("smallmoney", 10, 4, "-214748.3649")]
    [InlineData("money", 19, 4, "922337203685477.5808")]
    [InlineData("money", 19, 4, "-922337203685477.5809")]
    public void Money_values_outside_provider_ranges_are_diagnostics(string providerType, byte precision, byte scale, string value)
    {
        var sourceId = Guid.NewGuid();
        var schema = new DataSourceSchema(Guid.NewGuid(), DateTimeOffset.UtcNow,
        [new SchemaDatabaseObject(new("dbo", "Values"), DatabaseObjectKind.Table,
        [new SchemaColumn("Amount", 1, NormalizedTypeCategory.Decimal, providerType, false, 8, precision, scale, SchemaColumnCapabilities.Select | SchemaColumnCapabilities.Filter)])]);
        var definition = new QueryDefinition(QueryDefinition.CurrentVersion, new(sourceId, new("dbo", "Values")), [new(sourceId, "Amount", "Amount")], [new(sourceId, "Amount", QueryFilterOperator.Equal, [value])]);

        var result = QueryEngine.Prepare(schema, definition);

        Assert.Contains(result.Diagnostics, item => item.Code == "query.filter.value-incompatible");
    }

    [Theory]
    [InlineData("smallmoney", 10, 4, "214748.3647")]
    [InlineData("money", 19, 4, "922337203685477.5807")]
    public void Money_values_at_provider_boundaries_are_valid(string providerType, byte precision, byte scale, string value)
    {
        var sourceId = Guid.NewGuid();
        var schema = new DataSourceSchema(Guid.NewGuid(), DateTimeOffset.UtcNow,
        [new SchemaDatabaseObject(new("dbo", "Values"), DatabaseObjectKind.Table,
        [new SchemaColumn("Amount", 1, NormalizedTypeCategory.Decimal, providerType, false, 8, precision, scale, SchemaColumnCapabilities.Select | SchemaColumnCapabilities.Filter)])]);
        var definition = new QueryDefinition(QueryDefinition.CurrentVersion, new(sourceId, new("dbo", "Values")), [new(sourceId, "Amount", "Amount")], [new(sourceId, "Amount", QueryFilterOperator.Equal, [value])]);

        Assert.True(QueryEngine.Prepare(schema, definition).IsValid);
    }

    [Fact]
    public void Nested_boolean_expression_lowers_without_flattening_its_meaning()
    {
        var sourceId = Guid.NewGuid();
        var first = new QueryFilter(sourceId, "OrderId", QueryFilterOperator.GreaterThan, ["10"]);
        var second = new QueryFilter(sourceId, "OrderId", QueryFilterOperator.LessThan, ["20"]);
        var expression = new QueryFilterGroup(Guid.NewGuid(), true, QueryFilterGroupOperator.Or,
        [
            new QueryFilterCondition(Guid.NewGuid(), true, first),
            new QueryFilterNot(Guid.NewGuid(), true, new QueryFilterCondition(Guid.NewGuid(), true, second)),
        ]);
        var definition = new QueryDefinition(QueryDefinition.CurrentVersion, new(sourceId, new("sales", "Orders")), [new(sourceId, "OrderId", "OrderId")], FilterExpression: expression);

        var result = QueryEngine.Prepare(SchemaWithOrders(), definition);

        Assert.True(result.IsValid);
        var group = Assert.IsType<ExecutableFilterGroup>(result.Query!.FilterExpression);
        Assert.Equal(QueryFilterGroupOperator.Or, group.Operator);
        Assert.IsType<ExecutableFilterNot>(group.Children[1]);
    }

    [Fact]
    public void Disabled_invalid_branches_are_preserved_but_do_not_validate_or_execute()
    {
        var sourceId = Guid.NewGuid();
        var disabled = new QueryFilterCondition(Guid.NewGuid(), false, new(sourceId, "Missing", QueryFilterOperator.Equal, ["bad"]));
        var disabledGroup = new QueryFilterGroup(Guid.NewGuid(), true, QueryFilterGroupOperator.And, [disabled]);
        var expression = new QueryFilterGroup(Guid.NewGuid(), true, QueryFilterGroupOperator.And, [new QueryFilterNot(Guid.NewGuid(), true, disabledGroup)]);
        var definition = new QueryDefinition(QueryDefinition.CurrentVersion, new(sourceId, new("sales", "Orders")), [new(sourceId, "OrderId", "OrderId")], FilterExpression: expression);

        var result = QueryEngine.Prepare(SchemaWithOrders(), definition);

        Assert.True(result.IsValid);
        Assert.Empty(result.Query!.Filters);
        Assert.Null(result.Query.FilterExpression);
        var json = System.Text.Json.JsonSerializer.Serialize(definition);
        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<QueryDefinition>(json)!;
        var restored = Assert.IsType<QueryFilterGroup>(roundTrip.FilterExpression);
        var restoredNot = Assert.IsType<QueryFilterNot>(restored.Children[0]);
        var restoredGroup = Assert.IsType<QueryFilterGroup>(restoredNot.Operand);
        Assert.False(Assert.IsType<QueryFilterCondition>(restoredGroup.Children[0]).IsEnabled);
    }

    [Fact]
    public void Empty_groups_return_a_focused_diagnostic()
    {
        var sourceId = Guid.NewGuid();
        var definition = new QueryDefinition(QueryDefinition.CurrentVersion, new(sourceId, new("sales", "Orders")), [new(sourceId, "OrderId", "OrderId")],
            FilterExpression: new QueryFilterGroup(Guid.NewGuid(), true, QueryFilterGroupOperator.Or, []));

        var result = QueryEngine.Prepare(SchemaWithOrders(), definition);

        Assert.False(result.IsValid);
        Assert.Equal("query.filter.group-empty", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Parameter_reference_uses_a_runtime_value_without_changing_the_definition()
    {
        var sourceId = Guid.NewGuid();
        var parameter = new QueryParameterDefinition("minimumOrderId", "Minimum order ID", NormalizedTypeCategory.Integer, true, null);
        var filter = new QueryFilter(sourceId, "OrderId", QueryFilterOperator.GreaterThan, [], [new QueryParameterReference("minimumOrderId")]);
        var definition = new QueryDefinition(
            QueryDefinition.CurrentVersion,
            new(sourceId, new("sales", "Orders")),
            [new(sourceId, "OrderId", "OrderId")],
            FilterExpression: new QueryFilterCondition(Guid.NewGuid(), true, filter),
            Parameters: [parameter]);

        var result = QueryEngine.Prepare(SchemaWithOrders(), definition, new Dictionary<string, string?> { ["MINIMUMORDERID"] = "42" });

        Assert.True(result.IsValid);
        Assert.Equal(42, Assert.Single(Assert.Single(result.Query!.Filters).Values));
        Assert.Equal("minimumOrderId", Assert.IsType<QueryParameterReference>(Assert.Single(filter.AvailableOperands)).Name);
    }

    [Fact]
    public void Invalid_parameter_declarations_and_missing_required_values_return_focused_diagnostics()
    {
        var sourceId = Guid.NewGuid();
        var definition = new QueryDefinition(
            QueryDefinition.CurrentVersion,
            new(sourceId, new("sales", "Orders")),
            [new(sourceId, "OrderId", "OrderId")],
            Parameters:
            [
                new("minimum", "Minimum", NormalizedTypeCategory.Integer, true, null),
                new("MINIMUM", "Duplicate", NormalizedTypeCategory.Integer, false, null),
                new("bad name", "", NormalizedTypeCategory.Unknown, false, "x"),
                new("fallback", "Fallback", NormalizedTypeCategory.Integer, false, "not-an-integer"),
            ]);

        var result = QueryEngine.Prepare(SchemaWithOrders(), definition);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, item => item.Code == "query.parameter.name-duplicate");
        Assert.Contains(result.Diagnostics, item => item.Code == "query.parameter.name-invalid");
        Assert.Contains(result.Diagnostics, item => item.Code == "query.parameter.display-name-required");
        Assert.Contains(result.Diagnostics, item => item.Code == "query.parameter.type-unsupported");
        Assert.Contains(result.Diagnostics, item => item.Code == "query.parameter.default-invalid");
        Assert.Contains(result.Diagnostics, item => item.Code == "query.parameter.value-required");
    }

    [Fact]
    public void Defaults_and_repeated_runtime_values_prepare_independent_queries()
    {
        var sourceId = Guid.NewGuid();
        var definition = new QueryDefinition(
            QueryDefinition.CurrentVersion,
            new(sourceId, new("sales", "Orders")),
            [new(sourceId, "OrderId", "OrderId")],
            FilterExpression: new QueryFilterCondition(Guid.NewGuid(), true,
                new(sourceId, "OrderId", QueryFilterOperator.Equal, [], [new QueryParameterReference("orderId")])),
            Parameters: [new("orderId", "Order ID", NormalizedTypeCategory.Integer, false, "7")]);

        var withDefault = QueryEngine.Prepare(SchemaWithOrders(), definition);
        var firstRun = QueryEngine.Prepare(SchemaWithOrders(), definition, new Dictionary<string, string?> { ["orderId"] = "8" });
        var secondRun = QueryEngine.Prepare(SchemaWithOrders(), definition, new Dictionary<string, string?> { ["orderId"] = "9" });

        Assert.Equal(7, Assert.Single(Assert.Single(withDefault.Query!.Filters).Values));
        Assert.Equal(8, Assert.Single(Assert.Single(firstRun.Query!.Filters).Values));
        Assert.Equal(9, Assert.Single(Assert.Single(secondRun.Query!.Filters).Values));
        Assert.Equal("7", definition.AvailableParameters[0].DefaultValue);
    }

    [Fact]
    public void Unknown_and_type_incompatible_parameter_references_are_diagnostics()
    {
        var sourceId = Guid.NewGuid();
        var definition = new QueryDefinition(
            QueryDefinition.CurrentVersion,
            new(sourceId, new("sales", "Orders")),
            [new(sourceId, "OrderId", "OrderId")],
            FilterExpression: new QueryFilterGroup(Guid.NewGuid(), true, QueryFilterGroupOperator.And,
            [
                new QueryFilterCondition(Guid.NewGuid(), true, new(sourceId, "OrderId", QueryFilterOperator.Equal, [], [new QueryParameterReference("missing")])),
                new QueryFilterCondition(Guid.NewGuid(), true, new(sourceId, "OrderId", QueryFilterOperator.Equal, [], [new QueryParameterReference("textValue")])),
            ]),
            Parameters: [new("textValue", "Text value", NormalizedTypeCategory.Text, true, null)]);

        var result = QueryEngine.Prepare(SchemaWithOrders(), definition, new Dictionary<string, string?> { ["textValue"] = "42" });

        Assert.Contains(result.Diagnostics, item => item.Code == "query.parameter.reference-unknown");
        Assert.Contains(result.Diagnostics, item => item.Code == "query.parameter.type-incompatible");
    }

    [Fact]
    public void Runtime_parameter_values_coerce_to_supported_types_once_per_run()
    {
        var sourceId = Guid.NewGuid();
        var columns = new[]
        {
            new SchemaColumn("Text", 1, NormalizedTypeCategory.Text, "nvarchar", false, 100, null, null, SchemaColumnCapabilities.Select | SchemaColumnCapabilities.Filter),
            new SchemaColumn("Integer", 2, NormalizedTypeCategory.Integer, "int", false, 4, 10, 0, SchemaColumnCapabilities.Filter),
            new SchemaColumn("Decimal", 3, NormalizedTypeCategory.Decimal, "decimal", false, 9, 10, 2, SchemaColumnCapabilities.Filter),
            new SchemaColumn("Floating", 4, NormalizedTypeCategory.FloatingPoint, "float", false, 8, 53, null, SchemaColumnCapabilities.Filter),
            new SchemaColumn("Boolean", 5, NormalizedTypeCategory.Boolean, "bit", false, 1, null, null, SchemaColumnCapabilities.Filter),
            new SchemaColumn("Date", 6, NormalizedTypeCategory.Date, "date", false, 3, null, null, SchemaColumnCapabilities.Filter),
            new SchemaColumn("DateTime", 7, NormalizedTypeCategory.DateTime, "datetimeoffset", false, 10, null, 7, SchemaColumnCapabilities.Filter),
            new SchemaColumn("Guid", 8, NormalizedTypeCategory.Guid, "uniqueidentifier", false, 16, null, null, SchemaColumnCapabilities.Filter),
        };
        var schema = new DataSourceSchema(Guid.NewGuid(), DateTimeOffset.UtcNow, [new(new("dbo", "Values"), DatabaseObjectKind.Table, columns)]);
        var definitions = columns.Select(column => new QueryParameterDefinition(column.Name, column.Name, column.NormalizedType, true, null)).ToArray();
        var filters = columns.Select(column => (QueryFilterExpression)new QueryFilterCondition(Guid.NewGuid(), true,
            new(sourceId, column.Name, QueryFilterOperator.Equal, [], [new QueryParameterReference(column.Name)]))).ToArray();
        var definition = new QueryDefinition(QueryDefinition.CurrentVersion, new(sourceId, new("dbo", "Values")), [new(sourceId, "Text", "Text")],
            FilterExpression: new QueryFilterGroup(Guid.NewGuid(), true, QueryFilterGroupOperator.And, filters), Parameters: definitions);
        var values = new Dictionary<string, string?>
        {
            ["Text"] = "alpha", ["Integer"] = "42", ["Decimal"] = "12.34", ["Floating"] = "1.5", ["Boolean"] = "true",
            ["Date"] = "2026-08-27", ["DateTime"] = "2026-08-27T20:30:00+02:00", ["Guid"] = "11111111-1111-1111-1111-111111111111",
        };

        var result = QueryEngine.Prepare(schema, definition, values);

        Assert.True(result.IsValid);
        Assert.Equal([typeof(string), typeof(int), typeof(decimal), typeof(double), typeof(bool), typeof(DateOnly), typeof(DateTimeOffset), typeof(Guid)], result.Query!.Filters.Select(item => Assert.Single(item.Values).GetType()));
    }

    [Fact]
    public void Invalid_runtime_parameter_value_returns_one_focused_diagnostic()
    {
        var sourceId = Guid.NewGuid();
        var definition = new QueryDefinition(QueryDefinition.CurrentVersion, new(sourceId, new("sales", "Orders")), [new(sourceId, "OrderId", "OrderId")],
            FilterExpression: new QueryFilterCondition(Guid.NewGuid(), true, new(sourceId, "OrderId", QueryFilterOperator.Equal, [], [new QueryParameterReference("orderId")])),
            Parameters: [new("orderId", "Order ID", NormalizedTypeCategory.Integer, true, null)]);

        var result = QueryEngine.Prepare(SchemaWithOrders(), definition, new Dictionary<string, string?> { ["orderId"] = "not-an-integer" });

        Assert.Equal("query.parameter.value-invalid", Assert.Single(result.Diagnostics).Code);
    }

    private static DataSourceSchema SchemaWithOrders() =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow,
        [
            new SchemaDatabaseObject(
                new("sales", "Orders"),
                DatabaseObjectKind.Table,
                [new SchemaColumn("OrderId", 1, NormalizedTypeCategory.Integer, "int", false, 4, 10, 0, SchemaColumnCapabilities.Select | SchemaColumnCapabilities.Filter)]),
        ]);
}
