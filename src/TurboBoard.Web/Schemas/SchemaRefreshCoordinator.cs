using System.Collections.Concurrent;

namespace TurboBoard.Web.Schemas;

internal sealed class SchemaRefreshCoordinator
{
    private readonly ConcurrentDictionary<Guid, Lazy<Task<SchemaRefreshResult>>> refreshes = new();

    public async Task<SchemaRefreshResult> RunAsync(
        Guid dataSourceId,
        Func<CancellationToken, Task<SchemaRefreshResult>> refresh,
        CancellationToken waiterCancellationToken)
    {
        var pending = refreshes.GetOrAdd(
            dataSourceId,
            _ => new Lazy<Task<SchemaRefreshResult>>(
                () => refresh(CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));
        var operation = pending.Value;
        _ = operation.ContinueWith(
            _ => refreshes.TryRemove(
                new KeyValuePair<Guid, Lazy<Task<SchemaRefreshResult>>>(dataSourceId, pending)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return await operation.WaitAsync(waiterCancellationToken);
    }
}
