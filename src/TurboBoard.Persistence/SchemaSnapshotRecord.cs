namespace TurboBoard.Persistence;

public sealed class SchemaSnapshotRecord
{
    public Guid DataSourceId { get; set; }

    public Guid ConfigurationVersion { get; set; }

    public string SchemaJson { get; set; } = string.Empty;

    public DateTimeOffset DiscoveredAtUtc { get; set; }

    public string? LastRefreshFailureStatus { get; set; }

    public string? LastRefreshFailureMessage { get; set; }

    public DateTimeOffset? LastRefreshAttemptedAtUtc { get; set; }
}
