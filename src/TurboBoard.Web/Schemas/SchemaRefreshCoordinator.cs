using System.Collections.Concurrent;

namespace TurboBoard.Web.Schemas;

internal sealed class SchemaRefreshCoordinator
{
    private readonly ConcurrentDictionary<Guid, Lazy<Task<SchemaRefreshResult>>> refreshes = new();

    public async Task<SchemaRefreshResult> RunAsync(
        Guid dataSourceId,
        Func<Task<SchemaRefreshResult>> refresh)
    {
        var pending = refreshes.GetOrAdd(
            dataSourceId,
            _ => new Lazy<Task<SchemaRefreshResult>>(refresh, LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return await pending.Value;
        }
        finally
        {
            _ = refreshes.TryRemove(new KeyValuePair<Guid, Lazy<Task<SchemaRefreshResult>>>(dataSourceId, pending));
        }
    }
}
