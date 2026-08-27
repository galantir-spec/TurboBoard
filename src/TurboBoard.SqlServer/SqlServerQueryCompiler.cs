using Microsoft.Data.SqlClient;
using TurboBoard.Core.Queries;

namespace TurboBoard.SqlServer;

public sealed class SqlServerQueryCompiler : IQueryCompiler
{
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
        var commandText = $"SELECT TOP ({previewLimit + 1}) {string.Join(", ", selections)} " +
            $"FROM {quote.QuoteIdentifier(query.Source.QualifiedName.Schema)}.{quote.QuoteIdentifier(query.Source.QualifiedName.Name)} AS [q];";
        return new SqlServerCompiledQuery(commandText, previewLimit, columns);
    }
}

internal sealed record SqlServerCompiledQuery(
    string InspectionText,
    int PreviewLimit,
    IReadOnlyList<DynamicResultColumn> Columns) : ICompiledQuery;
