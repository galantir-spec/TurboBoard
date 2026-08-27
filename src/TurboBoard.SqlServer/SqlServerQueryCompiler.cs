using Microsoft.Data.SqlClient;
using TurboBoard.Core.Queries;

namespace TurboBoard.SqlServer;

public sealed class SqlServerQueryCompiler : IQueryCompiler
{
    private const int MaximumParameterCount = 2100;
    public string ProviderKey => SqlServerConnectionTester.SqlServerProviderKey;

    public ICompiledQuery Compile(ExecutableQuery query, int previewLimit)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(previewLimit, 1);
        var quote = new SqlCommandBuilder();
        var columns = query.Selections.Select((selection, ordinal) => new DynamicResultColumn(
            ordinal,
            selection.OutputName,
            selection.Column.NormalizedType,
            selection.Column.ProviderType,
            selection.Column.IsNullable)).ToArray();
        var selections = query.Selections.Select(selection =>
            $"[q].{quote.QuoteIdentifier(selection.Column.Name)} AS {quote.QuoteIdentifier(selection.OutputName)}");
        var parameters = new List<QueryParameterSpecification>();
        var predicates = query.Filters.Select(filter => CompileFilter(filter, quote, parameters)).ToArray();
        if (parameters.Count > MaximumParameterCount)
            throw new QueryCompilationException([new("query.filter.parameter-limit", $"SQL Server supports at most {MaximumParameterCount:N0} parameters in one Query Preview. Reduce the filter values.")]);
        var where = predicates.Length == 0 ? string.Empty : $" WHERE {string.Join(" AND ", predicates)}";
        var commandText = $"SELECT TOP ({previewLimit + 1}) {string.Join(", ", selections)} " +
            $"FROM {quote.QuoteIdentifier(query.Source.QualifiedName.Schema)}.{quote.QuoteIdentifier(query.Source.QualifiedName.Name)} AS [q]{where};";
        return new SqlServerCompiledQuery(commandText, previewLimit, columns, parameters);
    }

    private static string CompileFilter(ExecutableFilter filter, SqlCommandBuilder quote, List<QueryParameterSpecification> parameters)
    {
        var column = $"[q].{quote.QuoteIdentifier(filter.Column.Name)}";
        if (filter.Operator == QueryFilterOperator.IsNull) return $"{column} IS NULL";
        if (filter.Operator == QueryFilterOperator.IsNotNull) return $"{column} IS NOT NULL";
        var names = filter.Values.Select(value => AddParameter(filter, value, parameters)).ToArray();
        return filter.Operator switch
        {
            QueryFilterOperator.Equal => $"{column} = {names[0]}",
            QueryFilterOperator.NotEqual => $"{column} <> {names[0]}",
            QueryFilterOperator.LessThan => $"{column} < {names[0]}",
            QueryFilterOperator.LessThanOrEqual => $"{column} <= {names[0]}",
            QueryFilterOperator.GreaterThan => $"{column} > {names[0]}",
            QueryFilterOperator.GreaterThanOrEqual => $"{column} >= {names[0]}",
            QueryFilterOperator.Like => $"{column} LIKE {names[0]}",
            QueryFilterOperator.NotLike => $"{column} NOT LIKE {names[0]}",
            QueryFilterOperator.Contains or QueryFilterOperator.StartsWith or QueryFilterOperator.EndsWith => $"{column} LIKE {names[0]} ESCAPE '\\'",
            QueryFilterOperator.In => $"{column} IN ({string.Join(", ", names)})",
            QueryFilterOperator.NotIn => $"{column} NOT IN ({string.Join(", ", names)})",
            QueryFilterOperator.Between => $"{column} BETWEEN {names[0]} AND {names[1]}",
            _ => throw new InvalidOperationException("Unsupported validated filter operator."),
        };
    }

    private static string AddParameter(ExecutableFilter filter, object value, List<QueryParameterSpecification> parameters)
    {
        var name = $"@p{parameters.Count}";
        var parameterValue = filter.Operator switch
        {
            QueryFilterOperator.Contains => $"%{EscapeLikeLiteral((string)value)}%",
            QueryFilterOperator.StartsWith => $"{EscapeLikeLiteral((string)value)}%",
            QueryFilterOperator.EndsWith => $"%{EscapeLikeLiteral((string)value)}",
            _ => value,
        };
        var maximumLength = parameterValue is string text && filter.Operator is QueryFilterOperator.Contains or QueryFilterOperator.StartsWith or QueryFilterOperator.EndsWith
            ? filter.Column.MaximumLength is > 0 ? Math.Max(filter.Column.MaximumLength.Value, text.Length) : filter.Column.MaximumLength
            : filter.Column.MaximumLength;
        parameters.Add(new(name, parameterValue, filter.Column.ProviderType, maximumLength, filter.Column.Precision, filter.Column.Scale));
        return name;
    }

    private static string EscapeLikeLiteral(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal)
        .Replace("[", "\\[", StringComparison.Ordinal);
}

internal sealed record SqlServerCompiledQuery(
    string InspectionText,
    int PreviewLimit,
    IReadOnlyList<DynamicResultColumn> Columns,
    IReadOnlyList<QueryParameterSpecification> Parameters) : ICompiledQuery;
