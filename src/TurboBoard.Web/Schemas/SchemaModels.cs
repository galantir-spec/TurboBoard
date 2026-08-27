using TurboBoard.Core.Schemas;

namespace TurboBoard.Web.Schemas;

public enum SchemaRefreshStatus
{
    Succeeded,
    Failed,
}

public sealed record SchemaRefreshResult(
    SchemaRefreshStatus Status,
    string Message,
    DataSourceSchema? Schema,
    SchemaDiscoveryStatus? FailureStatus = null);

public sealed record SchemaState(
    DataSourceSchema Schema,
    SchemaDiscoveryStatus? LastRefreshFailureStatus,
    string? LastRefreshFailureMessage,
    DateTimeOffset? LastRefreshAttemptedAtUtc);

public interface ISchemaService
{
    Task<DataSourceSchema?> GetAsync(Guid dataSourceId, CancellationToken cancellationToken = default);

    Task<SchemaState?> GetStateAsync(Guid dataSourceId, CancellationToken cancellationToken = default);

    Task<SchemaRefreshResult> RefreshAsync(Guid dataSourceId, CancellationToken cancellationToken = default);
}
