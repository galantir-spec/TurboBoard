using System.Collections.Concurrent;

namespace TurboBoard.Web.Schemas;

internal sealed class SchemaRefreshCoordinator
{
    private readonly ConcurrentDictionary<Guid, RefreshOperation> refreshes = new();

    public async Task<SchemaRefreshResult> RunAsync(
        Guid dataSourceId,
        Func<CancellationToken, Task<SchemaRefreshResult>> refresh,
        CancellationToken waiterCancellationToken)
    {
        var pending = refreshes.GetOrAdd(
            dataSourceId,
            _ => new RefreshOperation(refresh));
        pending.AddWaiter();
        var operation = pending.Task;
        _ = operation.ContinueWith(
            _ => refreshes.TryRemove(
                new KeyValuePair<Guid, RefreshOperation>(dataSourceId, pending)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        try
        {
            return await operation.WaitAsync(waiterCancellationToken);
        }
        finally
        {
            if (pending.RemoveWaiter() == 0 && !operation.IsCompleted)
            {
                refreshes.TryRemove(new KeyValuePair<Guid, RefreshOperation>(dataSourceId, pending));
                pending.Cancel();
            }
        }
    }

    private sealed class RefreshOperation
    {
        private readonly CancellationTokenSource cancellation = new();
        private readonly Lazy<Task<SchemaRefreshResult>> task;
        private int waiterCount;

        public RefreshOperation(Func<CancellationToken, Task<SchemaRefreshResult>> refresh) =>
            task = new(() => refresh(cancellation.Token), LazyThreadSafetyMode.ExecutionAndPublication);

        public Task<SchemaRefreshResult> Task => task.Value;
        public void AddWaiter() => Interlocked.Increment(ref waiterCount);
        public int RemoveWaiter() => Interlocked.Decrement(ref waiterCount);
        public void Cancel() => cancellation.Cancel();
    }
}
