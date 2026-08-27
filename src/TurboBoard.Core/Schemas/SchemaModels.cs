using TurboBoard.Core.DataSources;

namespace TurboBoard.Core.Schemas;

public sealed record QualifiedDatabaseObjectName(string Schema, string Name)
{
    public string DisplayName => $"{Schema}.{Name}";
}

public enum DatabaseObjectKind
{
    Table,
    View,
}

public enum NormalizedTypeCategory
{
    Unknown,
    Boolean,
    Integer,
    Decimal,
    FloatingPoint,
    Text,
    Date,
    DateTime,
    Time,
    Guid,
    Binary,
}

[Flags]
public enum SchemaColumnCapabilities
{
    None = 0,
    Select = 1,
    Filter = 2,
    Sort = 4,
    Group = 8,
    Aggregate = 16,
}

public sealed record SchemaColumn(
    string Name,
    int Ordinal,
    NormalizedTypeCategory NormalizedType,
    string ProviderType,
    bool IsNullable,
    int? MaximumLength,
    byte? Precision,
    byte? Scale,
    SchemaColumnCapabilities Capabilities);

public sealed record SchemaDatabaseObject(
    QualifiedDatabaseObjectName QualifiedName,
    DatabaseObjectKind Kind,
    IReadOnlyList<SchemaColumn> Columns);

public sealed record DataSourceSchema(
    Guid DataSourceId,
    DateTimeOffset DiscoveredAtUtc,
    IReadOnlyList<SchemaDatabaseObject> Objects);

public enum SchemaDiscoveryStatus
{
    Succeeded,
    Cancelled,
    InvalidConfiguration,
    AuthenticationFailed,
    CertificateValidationFailed,
    NetworkFailure,
    DatabaseUnavailable,
    UnexpectedFailure,
}

public sealed record SchemaDiscoveryResult(
    SchemaDiscoveryStatus Status,
    string Message,
    IReadOnlyList<SchemaDatabaseObject>? Objects = null)
{
    public static SchemaDiscoveryResult Succeeded(IReadOnlyList<SchemaDatabaseObject> objects) =>
        new(SchemaDiscoveryStatus.Succeeded, "Schema discovery succeeded.", objects);
}

public interface IDataSourceSchemaDiscoverer
{
    string ProviderKey { get; }

    Task<SchemaDiscoveryResult> DiscoverAsync(
        DataSourceConnectionRequest request,
        CancellationToken cancellationToken = default);
}
