using Microsoft.Extensions.Caching.Memory;
using TurboBoard.Core.Schemas;

namespace TurboBoard.Web.Schemas;

internal sealed record CachedSchema(DataSourceSchema Schema, Guid ConfigurationVersion);

internal sealed class SchemaMemoryCache(IMemoryCache cache)
{
    public bool TryGet(Guid dataSourceId, out CachedSchema? schema) =>
        cache.TryGetValue(CacheKey(dataSourceId), out schema);

    public void Set(DataSourceSchema schema, Guid configurationVersion) =>
        cache.Set(
            CacheKey(schema.DataSourceId),
            new CachedSchema(schema, configurationVersion),
            TimeSpan.FromMinutes(30));

    public void Remove(Guid dataSourceId) => cache.Remove(CacheKey(dataSourceId));

    private static string CacheKey(Guid dataSourceId) => $"schema:{dataSourceId}";
}
