using System.Data.Common;
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
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = await MaterializeAsync(reader, sqlQuery, TimeSpan.Zero, cancellationToken);
        stopwatch.Stop();
        return result with { Duration = stopwatch.Elapsed };
    }

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
