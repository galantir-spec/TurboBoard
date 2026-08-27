using Microsoft.Data.SqlClient;
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
    byte Scale);

public interface ISqlServerCatalogReader
{
    Task<IReadOnlyList<SqlServerCatalogColumn>> ReadAsync(
        SqlServerConnectionSettings settings,
        CancellationToken cancellationToken = default);
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
            is_nullable = c.is_nullable
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
                reader.GetByte(8)));
        }

        return columns;
    }
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
            var rows = await catalogReader.ReadAsync(
                SqlServerConnectionRequest.ToSettings(request),
                cancellationToken);
            return SchemaDiscoveryResult.Succeeded(Map(rows));
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

    private static IReadOnlyList<SchemaDatabaseObject> Map(IReadOnlyList<SqlServerCatalogColumn> rows) =>
        rows.GroupBy(row => new { row.SchemaName, row.ObjectName, row.ObjectType })
            .OrderBy(group => group.Key.SchemaName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key.ObjectName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SchemaDatabaseObject(
                new QualifiedDatabaseObjectName(group.Key.SchemaName, group.Key.ObjectName),
                group.Key.ObjectType is "V" or "VIEW" ? DatabaseObjectKind.View : DatabaseObjectKind.Table,
                group.OrderBy(row => row.Ordinal).Select(MapColumn).ToArray()))
            .ToArray();

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
            Capabilities(normalizedType));
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
