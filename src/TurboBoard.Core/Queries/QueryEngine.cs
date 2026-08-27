using System.Text.RegularExpressions;
using System.Globalization;
using TurboBoard.Core.Schemas;

namespace TurboBoard.Core.Queries;

public static partial class QueryEngine
{
    public static QueryPreparationResult Prepare(
        DataSourceSchema schema,
        QueryDefinition definition,
        IReadOnlyDictionary<string, string?>? parameterValues = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(definition);
        var diagnostics = new List<ValidationDiagnostic>();
        if (definition.Version != QueryDefinition.CurrentVersion)
        {
            diagnostics.Add(new("query.version.unsupported", "This Query Definition version is not supported."));
        }

        if (definition.Source.Id == Guid.Empty)
        {
            diagnostics.Add(new("query.source.id-required", "The selected source needs a stable identity."));
        }

        var source = schema.Objects.SingleOrDefault(item => item.QualifiedName == definition.Source.Object);
        if (source is null)
        {
            diagnostics.Add(new("query.source.unknown", $"Source '{definition.Source.Object.DisplayName}' is not in the current Schema."));
        }

        var logicalSources = new Dictionary<Guid, SchemaDatabaseObject>();
        if (source is not null && definition.Source.Id != Guid.Empty)
        {
            logicalSources[definition.Source.Id] = source;
        }
        var executableJoins = PrepareJoins(schema, definition.AvailableJoins, logicalSources, diagnostics);

        if (definition.Selections.Count == 0)
        {
            diagnostics.Add(new("query.selection.required", "Select at least one column."));
        }

        var executableSelections = new List<ExecutableSelection>();
        var outputNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selection in definition.Selections)
        {
            if (!logicalSources.TryGetValue(selection.SourceId, out var selectionSource))
            {
                diagnostics.Add(new("query.selection.source-unknown", $"Column '{selection.ColumnName}' refers to an unknown source."));
                continue;
            }

            var column = selectionSource.Columns.SingleOrDefault(item =>
                string.Equals(item.Name, selection.ColumnName, StringComparison.OrdinalIgnoreCase));
            if (column is null || !column.Capabilities.HasFlag(SchemaColumnCapabilities.Select))
            {
                diagnostics.Add(new("query.selection.column-unknown", $"Column '{selection.ColumnName}' is not selectable from the current Schema."));
                continue;
            }

            if (!OutputNamePattern().IsMatch(selection.OutputName))
            {
                diagnostics.Add(new("query.selection.alias-invalid", $"Output name '{selection.OutputName}' is not valid."));
                continue;
            }

            if (!outputNames.Add(selection.OutputName))
            {
                diagnostics.Add(new("query.selection.alias-duplicate", $"Output name '{selection.OutputName}' is used more than once."));
                continue;
            }

            executableSelections.Add(new(selection.SourceId, column, selection.OutputName));
        }

        QueryFilterExpression? definitionExpression = definition.FilterExpression;
        if (definitionExpression is null && definition.AvailableFilters.Count > 0)
        {
            definitionExpression = new QueryFilterGroup(
                Guid.Empty,
                true,
                QueryFilterGroupOperator.And,
                definition.AvailableFilters.Select(filter => (QueryFilterExpression)new QueryFilterCondition(Guid.Empty, true, filter)).ToArray());
        }
        var runtimeValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (parameterValues is not null) foreach (var item in parameterValues) runtimeValues.TryAdd(item.Key, item.Value);
        var parameters = ValidateParameters(definition.AvailableParameters, runtimeValues, diagnostics);
        var editorIds = new HashSet<Guid>();
        var executableExpression = PrepareExpression(definitionExpression, logicalSources, diagnostics, editorIds, validateIdentity: definition.FilterExpression is not null, parameters);
        var executableFilters = Flatten(executableExpression).ToArray();

        return diagnostics.Count == 0 && source is not null
            ? new(new ExecutableQuery(definition.Source.Id, source, executableJoins, executableSelections, executableFilters, executableExpression), diagnostics)
            : new(null, diagnostics);
    }

    private static IReadOnlyList<ExecutableJoin> PrepareJoins(
        DataSourceSchema schema,
        IReadOnlyList<QueryJoin> joins,
        IDictionary<Guid, SchemaDatabaseObject> logicalSources,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var executable = new List<ExecutableJoin>();
        var joinIds = new HashSet<Guid>();
        foreach (var join in joins)
        {
            var valid = true;
            if (!Enum.IsDefined(join.Type))
            {
                diagnostics.Add(new("query.join.type-invalid", "A join uses an unsupported join type."));
                valid = false;
            }
            if (join.Id == Guid.Empty || !joinIds.Add(join.Id))
            {
                diagnostics.Add(new("query.join.identity-invalid", "Each join needs a unique stable identity."));
                valid = false;
            }
            if (join.Source.Id == Guid.Empty || logicalSources.ContainsKey(join.Source.Id))
            {
                diagnostics.Add(new("query.join.source-identity-invalid", "Each joined source needs a unique stable identity."));
                valid = false;
            }
            var joinedSource = schema.Objects.SingleOrDefault(item => item.QualifiedName == join.Source.Object);
            if (joinedSource is null)
            {
                diagnostics.Add(new("query.join.source-unknown", $"Joined source '{join.Source.Object.DisplayName}' is not in the current Schema."));
                continue;
            }
            if (join.Equalities.Count == 0)
            {
                diagnostics.Add(new("query.join.equality-required", "A join needs at least one column equality."));
                valid = false;
            }

            var candidateSources = new Dictionary<Guid, SchemaDatabaseObject>(logicalSources);
            if (!candidateSources.TryAdd(join.Source.Id, joinedSource)) valid = false;
            var equalities = new List<ExecutableJoinEquality>();
            foreach (var equality in join.Equalities)
            {
                if (!candidateSources.TryGetValue(equality.LeftSourceId, out var leftSource)
                    || !candidateSources.TryGetValue(equality.RightSourceId, out var rightSource)
                    || equality.LeftSourceId == equality.RightSourceId
                    || equality.LeftSourceId != join.Source.Id && equality.RightSourceId != join.Source.Id)
                {
                    diagnostics.Add(new("query.join.disconnected", "Each join must connect its new logical source to an already connected source."));
                    valid = false;
                    continue;
                }
                var existingId = equality.LeftSourceId == join.Source.Id ? equality.RightSourceId : equality.LeftSourceId;
                if (!logicalSources.ContainsKey(existingId))
                {
                    diagnostics.Add(new("query.join.disconnected", "Join order leaves a logical source disconnected."));
                    valid = false;
                    continue;
                }
                var leftColumn = leftSource.Columns.SingleOrDefault(item => string.Equals(item.Name, equality.LeftColumnName, StringComparison.OrdinalIgnoreCase));
                var rightColumn = rightSource.Columns.SingleOrDefault(item => string.Equals(item.Name, equality.RightColumnName, StringComparison.OrdinalIgnoreCase));
                if (leftColumn is null || rightColumn is null)
                {
                    diagnostics.Add(new("query.join.column-unknown", "A join equality refers to a column that is not in the current Schema."));
                    valid = false;
                    continue;
                }
                if (leftColumn.NormalizedType == NormalizedTypeCategory.Unknown || leftColumn.NormalizedType != rightColumn.NormalizedType)
                {
                    diagnostics.Add(new("query.join.type-incompatible", $"Columns '{leftColumn.Name}' and '{rightColumn.Name}' are not type-compatible."));
                    valid = false;
                    continue;
                }
                equalities.Add(new(equality.LeftSourceId, leftColumn, equality.RightSourceId, rightColumn));
            }

            if (valid && join.RelationshipName is not null && !MatchesRelationship(schema, join, candidateSources))
            {
                diagnostics.Add(new("query.join.relationship-mismatch", $"Relationship '{join.RelationshipName}' does not match the selected join columns."));
                valid = false;
            }
            if (!valid) continue;
            logicalSources.Add(join.Source.Id, joinedSource);
            executable.Add(new(join.Id, join.Type, join.Source.Id, joinedSource, equalities));
        }
        return executable;
    }

    private static bool MatchesRelationship(DataSourceSchema schema, QueryJoin join, IReadOnlyDictionary<Guid, SchemaDatabaseObject> sources)
    {
        var pairs = join.Equalities.Select(equality =>
            (Left: sources[equality.LeftSourceId].QualifiedName, equality.LeftColumnName,
             Right: sources[equality.RightSourceId].QualifiedName, equality.RightColumnName)).ToArray();
        return schema.AvailableRelationships.Any(relationship =>
            string.Equals(relationship.Name, join.RelationshipName, StringComparison.Ordinal)
            && relationship.FromColumns.Count == pairs.Length
            && relationship.FromColumns.Select((column, index) =>
                pairs.Any(pair =>
                    pair.Left == relationship.FromObject && pair.LeftColumnName.Equals(column, StringComparison.OrdinalIgnoreCase)
                    && pair.Right == relationship.ToObject && pair.RightColumnName.Equals(relationship.ToColumns[index], StringComparison.OrdinalIgnoreCase)
                    || pair.Right == relationship.FromObject && pair.RightColumnName.Equals(column, StringComparison.OrdinalIgnoreCase)
                    && pair.Left == relationship.ToObject && pair.LeftColumnName.Equals(relationship.ToColumns[index], StringComparison.OrdinalIgnoreCase)))
                .All(item => item));
    }

    private static IReadOnlyDictionary<string, PreparedParameter> ValidateParameters(
        IReadOnlyList<QueryParameterDefinition> definitions,
        IReadOnlyDictionary<string, string?> runtimeValues,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var parameters = new Dictionary<string, PreparedParameter>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in definitions)
        {
            if (!OutputNamePattern().IsMatch(parameter.Name)) diagnostics.Add(new("query.parameter.name-invalid", $"Query Parameter name '{parameter.Name}' is not valid."));
            if (!names.Add(parameter.Name)) diagnostics.Add(new("query.parameter.name-duplicate", $"Query Parameter name '{parameter.Name}' is used more than once."));
            if (string.IsNullOrWhiteSpace(parameter.DisplayName)) diagnostics.Add(new("query.parameter.display-name-required", $"Query Parameter '{parameter.Name}' needs a display name."));
            var typeSupported = IsSupportedParameterType(parameter.Type);
            if (!typeSupported) diagnostics.Add(new("query.parameter.type-unsupported", $"Query Parameter '{parameter.Name}' uses an unsupported type."));
            object? defaultValue = null;
            var defaultValid = parameter.DefaultValue is null || typeSupported && TryParseParameterValue(parameter.Type, parameter.DefaultValue, out defaultValue);
            if (!defaultValid) diagnostics.Add(new("query.parameter.default-invalid", $"Default value for Query Parameter '{parameter.Name}' is not valid for {parameter.Type}."));
            var hasRuntimeValue = runtimeValues.TryGetValue(parameter.Name, out var runtimeValue) && runtimeValue is not null;
            var hasValue = hasRuntimeValue || parameter.DefaultValue is not null;
            var value = hasRuntimeValue ? null : defaultValue;
            if (parameter.IsRequired && !hasValue) diagnostics.Add(new("query.parameter.value-required", $"Query Parameter '{parameter.DisplayName}' needs a value."));
            if (hasRuntimeValue && typeSupported && !TryParseParameterValue(parameter.Type, runtimeValue!, out value)) diagnostics.Add(new("query.parameter.value-invalid", $"Value for Query Parameter '{parameter.DisplayName}' is not valid for {parameter.Type}."));
            if (!parameters.ContainsKey(parameter.Name)) parameters.Add(parameter.Name, new(parameter, hasValue, value));
        }
        return parameters;
    }

    private static bool IsSupportedParameterType(NormalizedTypeCategory type) => type is
        NormalizedTypeCategory.Text or
        NormalizedTypeCategory.Integer or
        NormalizedTypeCategory.Decimal or
        NormalizedTypeCategory.FloatingPoint or
        NormalizedTypeCategory.Boolean or
        NormalizedTypeCategory.Date or
        NormalizedTypeCategory.DateTime or
        NormalizedTypeCategory.Guid;

    private static bool TryParseParameterValue(NormalizedTypeCategory type, string value, out object? parsed)
    {
        parsed = value;
        switch (type)
        {
            case NormalizedTypeCategory.Text: return true;
            case NormalizedTypeCategory.Integer when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer): parsed = integer; return true;
            case NormalizedTypeCategory.Decimal when decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue): parsed = decimalValue; return true;
            case NormalizedTypeCategory.FloatingPoint when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floating) && double.IsFinite(floating): parsed = floating; return true;
            case NormalizedTypeCategory.Boolean when bool.TryParse(value, out var boolean): parsed = boolean; return true;
            case NormalizedTypeCategory.Date when DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date): parsed = date; return true;
            case NormalizedTypeCategory.DateTime when DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime): parsed = dateTime; return true;
            case NormalizedTypeCategory.Guid when Guid.TryParse(value, out var guid): parsed = guid; return true;
            default: parsed = null; return false;
        }
    }

    private static ExecutableFilterExpression? PrepareExpression(
        QueryFilterExpression? expression,
        IReadOnlyDictionary<Guid, SchemaDatabaseObject> sources,
        ICollection<ValidationDiagnostic> diagnostics,
        ISet<Guid> editorIds,
        bool validateIdentity,
        IReadOnlyDictionary<string, PreparedParameter> parameters)
    {
        if (expression is null || !expression.IsEnabled) return null;
        if (validateIdentity && (expression.Id == Guid.Empty || !editorIds.Add(expression.Id)))
        {
            diagnostics.Add(new("query.filter.identity-invalid", "Each enabled filter condition and group needs a unique stable identity."));
            return null;
        }
        switch (expression)
        {
            case QueryFilterCondition condition:
                var filter = PrepareFilter(condition.Filter, sources, diagnostics, parameters);
                return filter is null ? null : new ExecutableFilterCondition(filter);
            case QueryFilterNot not:
                if (not.Operand is null)
                {
                    diagnostics.Add(new("query.filter.not-empty", "NOT needs one condition or group."));
                    return null;
                }
                if (!not.Operand.IsEnabled) return null;
                var operand = PrepareExpression(not.Operand, sources, diagnostics, editorIds, validateIdentity, parameters);
                return operand is null ? null : new ExecutableFilterNot(operand);
            case QueryFilterGroup group:
                if (group.Children.Count == 0)
                {
                    diagnostics.Add(new("query.filter.group-empty", $"{group.Operator} group needs at least one condition or group."));
                    return null;
                }
                var children = group.Children.Select(child => PrepareExpression(child, sources, diagnostics, editorIds, validateIdentity, parameters)).Where(child => child is not null).Cast<ExecutableFilterExpression>().ToArray();
                return children.Length == 0 ? null : new ExecutableFilterGroup(group.Operator, children);
            default:
                diagnostics.Add(new("query.filter.expression-unknown", "The filter expression contains an unsupported node."));
                return null;
        }
    }

    private static ExecutableFilter? PrepareFilter(
        QueryFilter filter,
        IReadOnlyDictionary<Guid, SchemaDatabaseObject> sources,
        ICollection<ValidationDiagnostic> diagnostics,
        IReadOnlyDictionary<string, PreparedParameter> parameters)
    {
        if (!sources.TryGetValue(filter.SourceId, out var source)) { diagnostics.Add(new("query.filter.source-unknown", $"Filter column '{filter.ColumnName}' refers to an unknown source.")); return null; }
        var column = source.Columns.SingleOrDefault(item => string.Equals(item.Name, filter.ColumnName, StringComparison.OrdinalIgnoreCase));
        if (column is null || !column.Capabilities.HasFlag(SchemaColumnCapabilities.Filter)) { diagnostics.Add(new("query.filter.column-unknown", $"Column '{filter.ColumnName}' cannot be filtered in the current Schema.")); return null; }
        if (!IsCompatible(column.NormalizedType, filter.Operator)) { diagnostics.Add(new("query.filter.operator-incompatible", $"Operator '{filter.Operator}' is not compatible with {column.NormalizedType} column '{column.Name}'.")); return null; }
        var operands = filter.AvailableOperands;
        var requiredCount = RequiredValueCount(filter.Operator);
        if (filter.Operator is QueryFilterOperator.In or QueryFilterOperator.NotIn && operands.Count == 0) { diagnostics.Add(new("query.filter.values-required", $"Operator '{filter.Operator}' needs at least one value.")); return null; }
        if (requiredCount is not null && operands.Count != requiredCount) { diagnostics.Add(new("query.filter.value-count", $"Operator '{filter.Operator}' needs {requiredCount} value(s).")); return null; }
        var typedValues = new List<object>();
        foreach (var operand in operands)
        {
            if (operand is QueryFixedValue fixedValue)
            {
                if (fixedValue.Value is null) { diagnostics.Add(new("query.filter.null-comparison", "Use IS NULL or IS NOT NULL instead of a null fixed value.")); return null; }
                if (!TryParseValue(column, fixedValue.Value, out var typedValue)) { diagnostics.Add(new("query.filter.value-incompatible", $"Value '{fixedValue.Value}' is not valid for {column.NormalizedType} column '{column.Name}'.")); return null; }
                typedValues.Add(typedValue!);
            }
            else if (operand is QueryParameterReference reference && parameters.TryGetValue(reference.Name, out var parameter))
            {
                if (parameter.Definition.Type != column.NormalizedType)
                {
                    diagnostics.Add(new("query.parameter.type-incompatible", $"Query Parameter '{parameter.Definition.Name}' is not compatible with column '{column.Name}'."));
                    return null;
                }
                if (!parameter.HasValue) { diagnostics.Add(new("query.parameter.value-required", $"Query Parameter '{parameter.Definition.DisplayName}' needs a value for this filter.")); return null; }
                if (parameter.Value is null) return null;
                var canonicalValue = FormatParameterValue(parameter.Value);
                if (!TryParseValue(column, canonicalValue, out var adapted)) { diagnostics.Add(new("query.parameter.value-incompatible", $"Value for Query Parameter '{parameter.Definition.DisplayName}' is outside the range supported by column '{column.Name}'.")); return null; }
                typedValues.Add(adapted!);
            }
            else
            {
                diagnostics.Add(new("query.parameter.reference-unknown", "A filter refers to an unknown Query Parameter."));
                return null;
            }
        }
        return new(filter.SourceId, column, filter.Operator, typedValues);
    }

    private static string FormatParameterValue(object value) => value switch
    {
        string text => text,
        bool boolean => boolean.ToString(),
        long integer => integer.ToString(CultureInfo.InvariantCulture),
        decimal decimalValue => decimalValue.ToString(CultureInfo.InvariantCulture),
        double floating => floating.ToString("R", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        Guid guid => guid.ToString("D"),
        _ => throw new InvalidOperationException("Unsupported prepared Query Parameter value."),
    };

    private sealed record PreparedParameter(QueryParameterDefinition Definition, bool HasValue, object? Value);

    private static IEnumerable<ExecutableFilter> Flatten(ExecutableFilterExpression? expression) => expression switch
    {
        ExecutableFilterCondition condition => [condition.Filter],
        ExecutableFilterGroup group => group.Children.SelectMany(Flatten),
        ExecutableFilterNot not => Flatten(not.Operand),
        _ => [],
    };

    public static IReadOnlyList<QueryFilterOperator> OperatorsFor(NormalizedTypeCategory type)
    {
        var operators = Enum.GetValues<QueryFilterOperator>().Where(item => IsCompatible(type, item)).ToArray();
        return operators;
    }

    private static bool IsCompatible(NormalizedTypeCategory type, QueryFilterOperator op)
    {
        if (op is QueryFilterOperator.IsNull or QueryFilterOperator.IsNotNull or QueryFilterOperator.Equal or QueryFilterOperator.NotEqual or QueryFilterOperator.In or QueryFilterOperator.NotIn) return type != NormalizedTypeCategory.Unknown;
        if (op is QueryFilterOperator.Like or QueryFilterOperator.NotLike or QueryFilterOperator.Contains or QueryFilterOperator.StartsWith or QueryFilterOperator.EndsWith) return type == NormalizedTypeCategory.Text;
        return type is NormalizedTypeCategory.Integer or NormalizedTypeCategory.Decimal or NormalizedTypeCategory.FloatingPoint or NormalizedTypeCategory.Text or NormalizedTypeCategory.Date or NormalizedTypeCategory.DateTime or NormalizedTypeCategory.Time;
    }

    private static int? RequiredValueCount(QueryFilterOperator op) => op switch
    {
        QueryFilterOperator.IsNull or QueryFilterOperator.IsNotNull => 0,
        QueryFilterOperator.Between => 2,
        QueryFilterOperator.In or QueryFilterOperator.NotIn => null,
        _ => 1,
    };

    private static bool TryParseValue(SchemaColumn column, string value, out object? parsed)
    {
        parsed = value;
        switch (column.NormalizedType)
        {
            case NormalizedTypeCategory.Text:
                return column.MaximumLength is not > 0 || value.Length <= column.MaximumLength;
            case NormalizedTypeCategory.Boolean when bool.TryParse(value, out var boolean): parsed = boolean; return true;
            case NormalizedTypeCategory.Integer when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer): return TryConvertInteger(column.ProviderType, integer, out parsed);
            case NormalizedTypeCategory.Decimal when decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue): parsed = decimalValue; return FitsDecimal(decimalValue, column);
            case NormalizedTypeCategory.FloatingPoint: return TryParseFloatingPoint(column, value, out parsed);
            case NormalizedTypeCategory.Date when DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date): parsed = date; return true;
            case NormalizedTypeCategory.DateTime: return TryParseDateTime(column.ProviderType, value, out parsed);
            case NormalizedTypeCategory.Time when TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var time) && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1): parsed = time; return true;
            case NormalizedTypeCategory.Guid when Guid.TryParse(value, out var guid): parsed = guid; return true;
            case NormalizedTypeCategory.Binary:
                try { var bytes = Convert.FromBase64String(value); parsed = bytes; return column.MaximumLength is not > 0 || bytes.Length <= column.MaximumLength; } catch (FormatException) { return false; }
            default: return false;
        }
    }

    private static bool TryConvertInteger(string providerType, long value, out object? converted)
    {
        converted = value;
        switch (providerType.ToLowerInvariant())
        {
            case "tinyint" when value is >= byte.MinValue and <= byte.MaxValue: converted = (byte)value; return true;
            case "smallint" when value is >= short.MinValue and <= short.MaxValue: converted = (short)value; return true;
            case "int" when value is >= int.MinValue and <= int.MaxValue: converted = (int)value; return true;
            case "bigint": return true;
            default: return false;
        }
    }

    private static bool TryParseDateTime(string providerType, string value, out object? parsed)
    {
        parsed = null;
        if (providerType.Equals("datetimeoffset", StringComparison.OrdinalIgnoreCase))
        {
            if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var offset)) return false;
            parsed = offset;
            return true;
        }
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime)) return false;
        var valid = providerType.ToLowerInvariant() switch
        {
            "datetime" => dateTime >= new DateTime(1753, 1, 1) && dateTime <= new DateTime(9999, 12, 31, 23, 59, 59, 997),
            "smalldatetime" => dateTime >= new DateTime(1900, 1, 1) && dateTime <= new DateTime(2079, 6, 6, 23, 59, 29),
            "datetime2" => true,
            _ => false,
        };
        parsed = valid ? dateTime : null;
        return valid;
    }

    private static bool TryParseFloatingPoint(SchemaColumn column, string value, out object? parsed)
    {
        parsed = null;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)) return false;
        var usesSingle = column.ProviderType.Equals("real", StringComparison.OrdinalIgnoreCase)
            || column.ProviderType.Equals("float", StringComparison.OrdinalIgnoreCase) && column.Precision is <= 24;
        if (usesSingle)
        {
            if (number < -float.MaxValue || number > float.MaxValue) return false;
            var single = (float)number;
            if (!float.IsFinite(single)) return false;
            parsed = single;
            return true;
        }
        if (!column.ProviderType.Equals("float", StringComparison.OrdinalIgnoreCase)) return false;
        parsed = number;
        return true;
    }

    private static bool FitsDecimal(decimal value, SchemaColumn column)
    {
        var providerRangeValid = column.ProviderType.ToLowerInvariant() switch
        {
            "money" => value is >= -922337203685477.5808m and <= 922337203685477.5807m,
            "smallmoney" => value is >= -214748.3648m and <= 214748.3647m,
            _ => true,
        };
        if (!providerRangeValid) return false;
        if (column.Precision is not byte precision || column.Scale is not byte scale) return true;
        var parts = Math.Abs(value).ToString(CultureInfo.InvariantCulture).Split('.');
        var integralDigits = parts[0].TrimStart('0').Length;
        var fractionalDigits = parts.Length == 1 ? 0 : parts[1].Length;
        return fractionalDigits <= scale
            && integralDigits <= precision - scale
            && integralDigits + fractionalDigits <= precision;
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex OutputNamePattern();
}
