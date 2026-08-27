using Microsoft.Data.SqlClient;
using System.Data.Common;
using TurboBoard.Core.DataSources;
using TurboBoard.Core.Schemas;

namespace TurboBoard.SqlServer;

public sealed record SqlServerCatalogColumn(
    string SchemaName,
    string ObjectName,
    string ObjectType,
    string ColumnName,
    int Ordinal,
    string ProviderType,
    bool IsNullable,
    int MaximumLength,
    byte Precision,
    byte Scale,
    bool IsIdentity = false,
    bool IsComputed = false);

public sealed record SqlServerCatalogKey(
    string SchemaName,
    string ObjectName,
    string Name,
    bool IsPrimary,
    int Ordinal,
    string ColumnName);

public sealed record SqlServerCatalogIndexColumn(
    string SchemaName,
    string ObjectName,
    string Name,
    bool IsUnique,
    int KeyOrdinal,
    bool IsIncluded,
    string ColumnName);

public sealed record SqlServerCatalogRelationshipColumn(
    string Name,
    string FromSchema,
    string FromObject,
    int Ordinal,
    string FromColumn,
    string ToSchema,
    string ToObject,
    string ToColumn);

public interface ISqlServerCatalogReader
{
    Task<IReadOnlyList<SqlServerCatalogColumn>> ReadAsync(
        SqlServerConnectionSettings settings,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SqlServerCatalogKey>> ReadKeysAsync(
        SqlServerConnectionSettings settings,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SqlServerCatalogKey>>([]);

    Task<IReadOnlyList<SqlServerCatalogIndexColumn>> ReadIndexesAsync(
        SqlServerConnectionSettings settings,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SqlServerCatalogIndexColumn>>([]);

    Task<IReadOnlyList<SqlServerCatalogRelationshipColumn>> ReadRelationshipsAsync(
        SqlServerConnectionSettings settings,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SqlServerCatalogRelationshipColumn>>([]);
}

public sealed class SqlServerCatalogReader : ISqlServerCatalogReader
{
    private const string CatalogSql = """
        SELECT
            schema_name = s.name,
            object_name = o.name,
            object_type = o.type,
            column_name = c.name,
            column_ordinal = c.column_id,
            provider_type = t.name,
            maximum_length = c.max_length,
            numeric_precision = c.precision,
            numeric_scale = c.scale,
            is_nullable = c.is_nullable,
            is_identity = c.is_identity,
            is_computed = c.is_computed
        FROM sys.objects AS o
        INNER JOIN sys.schemas AS s ON s.schema_id = o.schema_id
        INNER JOIN sys.columns AS c ON c.object_id = o.object_id
        INNER JOIN sys.types AS t ON t.user_type_id = c.user_type_id
        WHERE o.type IN ('U', 'V')
          AND o.is_ms_shipped = 0
        ORDER BY s.name, o.name, c.column_id;
        """;

    public async Task<IReadOnlyList<SqlServerCatalogColumn>> ReadAsync(
        SqlServerConnectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(SqlServerConnectionString.Create(settings));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = CatalogSql;
        var columns = new List<SqlServerCatalogColumn>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new SqlServerCatalogColumn(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.GetBoolean(9),
                reader.GetInt16(6),
                reader.GetByte(7),
                reader.GetByte(8),
                reader.GetBoolean(10),
                reader.GetBoolean(11)));
        }

        return columns;
    }

    public Task<IReadOnlyList<SqlServerCatalogKey>> ReadKeysAsync(
        SqlServerConnectionSettings settings,
        CancellationToken cancellationToken = default) => ReadAsync(settings, @"
            SELECT s.name, o.name, i.name, i.is_primary_key, ic.key_ordinal, c.name
            FROM sys.indexes i
            JOIN sys.objects o ON o.object_id = i.object_id
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE o.type = 'U' AND i.is_unique = 1 AND i.is_hypothetical = 0 AND i.is_disabled = 0 AND i.has_filter = 0 AND ic.key_ordinal > 0
            ORDER BY s.name, o.name, i.name, ic.key_ordinal;
            ", MapKey, cancellationToken);

    public Task<IReadOnlyList<SqlServerCatalogIndexColumn>> ReadIndexesAsync(
        SqlServerConnectionSettings settings,
        CancellationToken cancellationToken = default) => ReadAsync(settings, @"
            SELECT s.name, o.name, i.name, i.is_unique, ic.key_ordinal, ic.is_included_column, c.name
            FROM sys.indexes i
            JOIN sys.objects o ON o.object_id = i.object_id
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE o.type = 'U' AND i.is_hypothetical = 0 AND i.name IS NOT NULL
            ORDER BY s.name, o.name, i.name, ic.is_included_column, ic.key_ordinal, ic.index_column_id;
            ", MapIndex, cancellationToken);

    public Task<IReadOnlyList<SqlServerCatalogRelationshipColumn>> ReadRelationshipsAsync(
        SqlServerConnectionSettings settings,
        CancellationToken cancellationToken = default) => ReadAsync(settings, @"
            SELECT fk.name, fs.name, fo.name, fkc.constraint_column_id, fc.name, ts.name, tro.name, tc.name
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.objects fo ON fo.object_id = fk.parent_object_id
            JOIN sys.schemas fs ON fs.schema_id = fo.schema_id
            JOIN sys.columns fc ON fc.object_id = fo.object_id AND fc.column_id = fkc.parent_column_id
            JOIN sys.objects tro ON tro.object_id = fk.referenced_object_id
            JOIN sys.schemas ts ON ts.schema_id = tro.schema_id
            JOIN sys.columns tc ON tc.object_id = tro.object_id AND tc.column_id = fkc.referenced_column_id
            ORDER BY fk.name, fkc.constraint_column_id;
            ", reader => new SqlServerCatalogRelationshipColumn(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7)), cancellationToken);

    private static async Task<IReadOnlyList<T>> ReadAsync<T>(SqlServerConnectionSettings settings, string sql, Func<SqlDataReader, T> map, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(SqlServerConnectionString.Create(settings));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var items = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) items.Add(map(reader));
        return items;
    }

    internal static SqlServerCatalogKey MapKey(DbDataReader reader) =>
        new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), reader.GetByte(4), reader.GetString(5));

    internal static SqlServerCatalogIndexColumn MapIndex(DbDataReader reader) =>
        new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), reader.GetByte(4), reader.GetBoolean(5), reader.GetString(6));
}

public sealed class SqlServerSchemaDiscoverer(ISqlServerCatalogReader catalogReader)
    : IDataSourceSchemaDiscoverer
{
    private const SchemaColumnCapabilities StandardCapabilities =
        SchemaColumnCapabilities.Select |
        SchemaColumnCapabilities.Filter |
        SchemaColumnCapabilities.Sort;

    public string ProviderKey => SqlServerConnectionTester.SqlServerProviderKey;

    public async Task<SchemaDiscoveryResult> DiscoverAsync(
        DataSourceConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.ProviderKey, ProviderKey, StringComparison.OrdinalIgnoreCase))
        {
            return Failure(SchemaDiscoveryStatus.InvalidConfiguration, "The selected Schema provider is not supported.");
        }

        try
        {
            var settings = SqlServerConnectionRequest.ToSettings(request);
            var catalogColumns = await catalogReader.ReadAsync(settings, cancellationToken);
            var keys = await catalogReader.ReadKeysAsync(settings, cancellationToken);
            var indexes = await catalogReader.ReadIndexesAsync(settings, cancellationToken);
            var relationships = await catalogReader.ReadRelationshipsAsync(settings, cancellationToken);
            return SchemaDiscoveryResult.Succeeded(
                MapObjects(catalogColumns, keys, indexes),
                MapRelationships(relationships));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(SchemaDiscoveryStatus.Cancelled, "Schema discovery cancelled.");
        }
        catch (ArgumentException)
        {
            return Failure(SchemaDiscoveryStatus.InvalidConfiguration, "The SQL Server settings are not valid for Schema discovery.");
        }
        catch (SqlException exception)
        {
            return Categorize(exception);
        }
        catch (Exception)
        {
            return Failure(
                SchemaDiscoveryStatus.UnexpectedFailure,
                "SQL Server could not provide Schema metadata.");
        }
    }

    private static IReadOnlyList<SchemaDatabaseObject> MapObjects(
        IReadOnlyList<SqlServerCatalogColumn> catalogColumns,
        IReadOnlyList<SqlServerCatalogKey> keys,
        IReadOnlyList<SqlServerCatalogIndexColumn> indexes) =>
        catalogColumns.GroupBy(column => new { column.SchemaName, column.ObjectName, column.ObjectType })
            .OrderBy(group => group.Key.SchemaName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key.ObjectName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SchemaDatabaseObject(
                new QualifiedDatabaseObjectName(group.Key.SchemaName, group.Key.ObjectName),
                group.Key.ObjectType is "V" or "VIEW" ? DatabaseObjectKind.View : DatabaseObjectKind.Table,
                group.OrderBy(column => column.Ordinal).Select(MapColumn).ToArray(),
                MapKeys(keys, group.Key.SchemaName, group.Key.ObjectName),
                MapIndexes(indexes, group.Key.SchemaName, group.Key.ObjectName)))
            .ToArray();

    private static IReadOnlyList<SchemaKey> MapKeys(
        IReadOnlyList<SqlServerCatalogKey> keys,
        string schemaName,
        string objectName) =>
        keys.Where(key => key.SchemaName == schemaName && key.ObjectName == objectName)
            .GroupBy(key => new { key.Name, key.IsPrimary })
            .Select(group => new SchemaKey(
                group.Key.Name,
                group.Key.IsPrimary ? SchemaKeyKind.Primary : SchemaKeyKind.Unique,
                group.OrderBy(column => column.Ordinal).Select(column => column.ColumnName).ToArray()))
            .ToArray();

    private static IReadOnlyList<SchemaIndex> MapIndexes(
        IReadOnlyList<SqlServerCatalogIndexColumn> indexes,
        string schemaName,
        string objectName) =>
        indexes.Where(index => index.SchemaName == schemaName && index.ObjectName == objectName)
            .GroupBy(index => new { index.Name, index.IsUnique })
            .Select(group => new SchemaIndex(
                group.Key.Name,
                group.Key.IsUnique,
                group.Where(column => !column.IsIncluded).OrderBy(column => column.KeyOrdinal).Select(column => column.ColumnName).ToArray(),
                group.Where(column => column.IsIncluded).Select(column => column.ColumnName).ToArray()))
            .ToArray();

    private static IReadOnlyList<SchemaRelationship> MapRelationships(IReadOnlyList<SqlServerCatalogRelationshipColumn> rows) =>
        rows.GroupBy(row => new { row.Name, row.FromSchema, row.FromObject, row.ToSchema, row.ToObject })
            .Select(group => new SchemaRelationship(group.Key.Name, new(group.Key.FromSchema, group.Key.FromObject), group.OrderBy(row => row.Ordinal).Select(row => row.FromColumn).ToArray(), new(group.Key.ToSchema, group.Key.ToObject), group.OrderBy(row => row.Ordinal).Select(row => row.ToColumn).ToArray())).ToArray();

    private static SchemaColumn MapColumn(SqlServerCatalogColumn row)
    {
        var normalizedType = Normalize(row.ProviderType);
        return new SchemaColumn(
            row.ColumnName,
            row.Ordinal,
            normalizedType,
            row.ProviderType,
            row.IsNullable,
            NormalizeLength(row.ProviderType, row.MaximumLength),
            row.Precision == 0 ? null : row.Precision,
            row.Precision == 0 ? null : row.Scale,
            Capabilities(normalizedType),
            row.IsIdentity,
            row.IsComputed);
    }

    private static int? NormalizeLength(string providerType, int maximumLength)
    {
        if (maximumLength < 0)
        {
            return null;
        }

        return providerType.Equals("nvarchar", StringComparison.OrdinalIgnoreCase) ||
            providerType.Equals("nchar", StringComparison.OrdinalIgnoreCase)
            ? maximumLength / 2
            : maximumLength;
    }

    private static NormalizedTypeCategory Normalize(string providerType) =>
        providerType.ToLowerInvariant() switch
        {
            "bit" => NormalizedTypeCategory.Boolean,
            "tinyint" or "smallint" or "int" or "bigint" => NormalizedTypeCategory.Integer,
            "decimal" or "numeric" or "money" or "smallmoney" => NormalizedTypeCategory.Decimal,
            "float" or "real" => NormalizedTypeCategory.FloatingPoint,
            "char" or "nchar" or "varchar" or "nvarchar" or "text" or "ntext" or "xml" => NormalizedTypeCategory.Text,
            "date" => NormalizedTypeCategory.Date,
            "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" => NormalizedTypeCategory.DateTime,
            "time" => NormalizedTypeCategory.Time,
            "uniqueidentifier" => NormalizedTypeCategory.Guid,
            "binary" or "varbinary" or "image" or "rowversion" or "timestamp" => NormalizedTypeCategory.Binary,
            _ => NormalizedTypeCategory.Unknown,
        };

    private static SchemaColumnCapabilities Capabilities(NormalizedTypeCategory type)
    {
        if (type == NormalizedTypeCategory.Unknown)
        {
            return SchemaColumnCapabilities.None;
        }

        var capabilities = StandardCapabilities;
        if (type is not NormalizedTypeCategory.Binary)
        {
            capabilities |= SchemaColumnCapabilities.Group;
        }

        if (type is NormalizedTypeCategory.Integer or NormalizedTypeCategory.Decimal or NormalizedTypeCategory.FloatingPoint)
        {
            capabilities |= SchemaColumnCapabilities.Aggregate;
        }

        return capabilities;
    }

    private static SchemaDiscoveryResult Categorize(SqlException exception) =>
        exception.Number switch
        {
            18456 => Failure(SchemaDiscoveryStatus.AuthenticationFailed, "SQL Server rejected the supplied credentials."),
            -2146893019 => Failure(SchemaDiscoveryStatus.CertificateValidationFailed, "SQL Server certificate validation failed."),
            -2 or 53 or 11001 or 10060 => Failure(SchemaDiscoveryStatus.NetworkFailure, "TurboBoard could not reach SQL Server."),
            4060 => Failure(SchemaDiscoveryStatus.DatabaseUnavailable, "The selected database is unavailable to this login."),
            _ => Failure(SchemaDiscoveryStatus.UnexpectedFailure, "SQL Server could not provide Schema metadata."),
        };

    private static SchemaDiscoveryResult Failure(SchemaDiscoveryStatus status, string message) =>
        new(status, message);
}
