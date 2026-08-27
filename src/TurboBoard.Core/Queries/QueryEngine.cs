using System.Text.RegularExpressions;
using System.Globalization;
using TurboBoard.Core.Schemas;

namespace TurboBoard.Core.Queries;

public static partial class QueryEngine
{
    public static QueryPreparationResult Prepare(DataSourceSchema schema, QueryDefinition definition)
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

        if (definition.Selections.Count == 0)
        {
            diagnostics.Add(new("query.selection.required", "Select at least one column."));
        }

        var executableSelections = new List<ExecutableSelection>();
        var outputNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selection in definition.Selections)
        {
            if (selection.SourceId != definition.Source.Id)
            {
                diagnostics.Add(new("query.selection.source-unknown", $"Column '{selection.ColumnName}' refers to an unknown source."));
                continue;
            }

            var column = source?.Columns.SingleOrDefault(item =>
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

            executableSelections.Add(new(column, selection.OutputName));
        }

        var executableFilters = new List<ExecutableFilter>();
        foreach (var filter in definition.AvailableFilters)
        {
            if (filter.SourceId != definition.Source.Id)
            {
                diagnostics.Add(new("query.filter.source-unknown", $"Filter column '{filter.ColumnName}' refers to an unknown source."));
                continue;
            }

            var column = source?.Columns.SingleOrDefault(item => string.Equals(item.Name, filter.ColumnName, StringComparison.OrdinalIgnoreCase));
            if (column is null || !column.Capabilities.HasFlag(SchemaColumnCapabilities.Filter))
            {
                diagnostics.Add(new("query.filter.column-unknown", $"Column '{filter.ColumnName}' cannot be filtered in the current Schema."));
                continue;
            }

            if (!IsCompatible(column.NormalizedType, filter.Operator))
            {
                diagnostics.Add(new("query.filter.operator-incompatible", $"Operator '{filter.Operator}' is not compatible with {column.NormalizedType} column '{column.Name}'."));
                continue;
            }

            var requiredCount = RequiredValueCount(filter.Operator);
            if (filter.Operator is QueryFilterOperator.In or QueryFilterOperator.NotIn && filter.Values.Count == 0)
            {
                diagnostics.Add(new("query.filter.values-required", $"Operator '{filter.Operator}' needs at least one value."));
                continue;
            }
            if (requiredCount is not null && filter.Values.Count != requiredCount)
            {
                diagnostics.Add(new("query.filter.value-count", $"Operator '{filter.Operator}' needs {requiredCount} value(s)."));
                continue;
            }

            var typedValues = new List<object>();
            var invalid = false;
            foreach (var value in filter.Values)
            {
                if (value is null)
                {
                    diagnostics.Add(new("query.filter.null-comparison", "Use IS NULL or IS NOT NULL instead of a null fixed value."));
                    invalid = true;
                    break;
                }
                if (!TryParseValue(column, value, out var typedValue))
                {
                    diagnostics.Add(new("query.filter.value-incompatible", $"Value '{value}' is not valid for {column.NormalizedType} column '{column.Name}'."));
                    invalid = true;
                    break;
                }
                typedValues.Add(typedValue!);
            }
            if (!invalid) executableFilters.Add(new(column, filter.Operator, typedValues));
        }

        return diagnostics.Count == 0 && source is not null
            ? new(new ExecutableQuery(definition.Source.Id, source, executableSelections, executableFilters), diagnostics)
            : new(null, diagnostics);
    }

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
