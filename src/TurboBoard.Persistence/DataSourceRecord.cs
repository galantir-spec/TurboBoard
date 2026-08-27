namespace TurboBoard.Persistence;

public sealed class DataSourceRecord
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string ProtectedSettings { get; set; } = string.Empty;

    public Guid ConfigurationVersion { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
