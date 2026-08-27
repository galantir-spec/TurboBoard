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
    SchemaColumnCapabilities Capabilities,
    bool IsIdentity = false,
    bool IsComputed = false);

public enum SchemaKeyKind
{
    Primary,
    Unique,
}

public sealed record SchemaKey(string Name, SchemaKeyKind Kind, IReadOnlyList<string> Columns);

public sealed record SchemaIndex(
    string Name,
    bool IsUnique,
    IReadOnlyList<string> KeyColumns,
    IReadOnlyList<string> IncludedColumns);

public sealed record SchemaRelationship(
    string Name,
    QualifiedDatabaseObjectName FromObject,
    IReadOnlyList<string> FromColumns,
    QualifiedDatabaseObjectName ToObject,
    IReadOnlyList<string> ToColumns);

public sealed record SchemaDatabaseObject(
    QualifiedDatabaseObjectName QualifiedName,
    DatabaseObjectKind Kind,
    IReadOnlyList<SchemaColumn> Columns,
    IReadOnlyList<SchemaKey>? Keys = null,
    IReadOnlyList<SchemaIndex>? Indexes = null)
{
    public IReadOnlyList<SchemaKey> AvailableKeys => Keys ?? [];
    public IReadOnlyList<SchemaIndex> AvailableIndexes => Indexes ?? [];
}

public sealed record DataSourceSchema(
    Guid DataSourceId,
    DateTimeOffset DiscoveredAtUtc,
    IReadOnlyList<SchemaDatabaseObject> Objects,
    IReadOnlyList<SchemaRelationship>? Relationships = null)
{
    public IReadOnlyList<SchemaRelationship> AvailableRelationships => Relationships ?? [];

    public IReadOnlyList<SchemaRelationship> FindRelationships(
        QualifiedDatabaseObjectName first,
        QualifiedDatabaseObjectName second) =>
        AvailableRelationships.Where(relationship =>
            (relationship.FromObject == first && relationship.ToObject == second) ||
            (relationship.FromObject == second && relationship.ToObject == first)).ToArray();
}

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
    IReadOnlyList<SchemaDatabaseObject>? Objects = null,
    IReadOnlyList<SchemaRelationship>? Relationships = null)
{
    public static SchemaDiscoveryResult Succeeded(IReadOnlyList<SchemaDatabaseObject> objects, IReadOnlyList<SchemaRelationship>? relationships = null) =>
        new(SchemaDiscoveryStatus.Succeeded, "Schema discovery succeeded.", objects, relationships);
}

public interface IDataSourceSchemaDiscoverer
{
    string ProviderKey { get; }

    Task<SchemaDiscoveryResult> DiscoverAsync(
        DataSourceConnectionRequest request,
        CancellationToken cancellationToken = default);
}
