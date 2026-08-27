using TurboBoard.Core.DataSources;

namespace TurboBoard.Core.Queries;

public sealed record DynamicResultRow(IReadOnlyList<object?> Values);

public sealed record DynamicResult(
    IReadOnlyList<DynamicResultColumn> Columns,
    IReadOnlyList<DynamicResultRow> Rows,
    TimeSpan Duration,
    bool WasTruncated)
{
    public int RowCount => Rows.Count;
}

public interface IQueryExecutor
{
    string ProviderKey { get; }

    Task<DynamicResult> ExecuteAsync(
        DataSourceConnectionRequest connection,
        ICompiledQuery query,
        CancellationToken cancellationToken = default);
}
