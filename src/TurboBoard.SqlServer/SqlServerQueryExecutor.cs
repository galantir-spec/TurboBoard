using System.Data.Common;
using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using TurboBoard.Core.DataSources;
using TurboBoard.Core.Queries;

namespace TurboBoard.SqlServer;

public sealed class SqlServerQueryExecutor : IQueryExecutor
{
    public string ProviderKey => SqlServerConnectionTester.SqlServerProviderKey;

    public async Task<DynamicResult> ExecuteAsync(
        DataSourceConnectionRequest connection,
        ICompiledQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(query);
        if (query is not SqlServerCompiledQuery sqlQuery)
        {
            throw new ArgumentException("The query plan was not produced by the SQL Server compiler.", nameof(query));
        }
        var stopwatch = Stopwatch.StartNew();
        var settings = SqlServerConnectionRequest.ToSettings(connection);
        await using var sqlConnection = new SqlConnection(SqlServerConnectionString.Create(settings));
        await sqlConnection.OpenAsync(cancellationToken);
        await using var command = sqlConnection.CreateCommand();
        command.CommandText = sqlQuery.InspectionText;
        AddParameters(command, sqlQuery.Parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = await MaterializeAsync(reader, sqlQuery, TimeSpan.Zero, cancellationToken);
        stopwatch.Stop();
        return result with { Duration = stopwatch.Elapsed };
    }

    internal static void AddParameters(SqlCommand command, IReadOnlyList<QueryParameterSpecification> specifications)
    {
        foreach (var specification in specifications)
        {
            var parameter = command.Parameters.Add(specification.Name, ToSqlDbType(specification.ProviderType));
            parameter.Value = specification.Value;
            if (specification.MaximumLength is int size && size != 0) parameter.Size = size;
            if (specification.Precision is byte precision) parameter.Precision = precision;
            if (specification.Scale is byte scale) parameter.Scale = scale;
        }
    }

    private static SqlDbType ToSqlDbType(string providerType) => providerType.ToLowerInvariant() switch
    {
        "bigint" => SqlDbType.BigInt,
        "binary" => SqlDbType.Binary,
        "bit" => SqlDbType.Bit,
        "char" => SqlDbType.Char,
        "date" => SqlDbType.Date,
        "datetime" => SqlDbType.DateTime,
        "datetime2" => SqlDbType.DateTime2,
        "datetimeoffset" => SqlDbType.DateTimeOffset,
        "decimal" or "numeric" => SqlDbType.Decimal,
        "float" => SqlDbType.Float,
        "image" => SqlDbType.Image,
        "int" => SqlDbType.Int,
        "money" => SqlDbType.Money,
        "nchar" => SqlDbType.NChar,
        "ntext" => SqlDbType.NText,
        "nvarchar" => SqlDbType.NVarChar,
        "real" => SqlDbType.Real,
        "smalldatetime" => SqlDbType.SmallDateTime,
        "smallint" => SqlDbType.SmallInt,
        "smallmoney" => SqlDbType.SmallMoney,
        "text" => SqlDbType.Text,
        "time" => SqlDbType.Time,
        "timestamp" or "rowversion" => SqlDbType.Timestamp,
        "tinyint" => SqlDbType.TinyInt,
        "uniqueidentifier" => SqlDbType.UniqueIdentifier,
        "varbinary" => SqlDbType.VarBinary,
        "varchar" => SqlDbType.VarChar,
        "xml" => SqlDbType.Xml,
        _ => throw new QueryCompilationException([new("query.filter.provider-type-unsupported", $"SQL Server type '{providerType}' cannot be bound safely as a filter parameter.")]),
    };

    internal static async Task<DynamicResult> MaterializeAsync(
        DbDataReader reader,
        SqlServerCompiledQuery query,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<DynamicResultRow>();
        var wasTruncated = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (rows.Count == query.PreviewLimit)
            {
                wasTruncated = true;
                break;
            }

            var rawValues = new object[reader.FieldCount];
            _ = reader.GetValues(rawValues);
            rows.Add(new(rawValues.Select(value => value is DBNull ? null : value).ToArray()));
        }

        var runtimeColumns = Enumerable.Range(0, reader.FieldCount)
            .Select(ordinal => new DynamicResultColumn(
                ordinal,
                reader.GetName(ordinal),
                query.Columns[ordinal].NormalizedType,
                reader.GetDataTypeName(ordinal),
                query.Columns[ordinal].IsNullable))
            .ToArray();
        return new(runtimeColumns, rows, duration, wasTruncated);
    }
}
