using Microsoft.Data.SqlClient;
using TurboBoard.Core.DataSources;

namespace TurboBoard.SqlServer;

public sealed class SqlServerConnectionTester : IDataSourceConnectionTester
{
    public const string SqlServerProviderKey = "sql-server";

    public string ProviderKey => SqlServerProviderKey;

    public Task<DataSourceConnectionTestResult> TestAsync(
        DataSourceConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.ProviderKey, ProviderKey, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new DataSourceConnectionTestResult(
                DataSourceConnectionTestStatus.InvalidConfiguration,
                "The selected Data Source provider is not supported by this connection tester."));
        }

        return TestAsync(ToSqlServerSettings(request), cancellationToken);
    }

    public async Task<DataSourceConnectionTestResult> TestAsync(
        SqlServerConnectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var connection = new SqlConnection(SqlServerConnectionString.Create(settings));
            await connection.OpenAsync(cancellationToken);
            return DataSourceConnectionTestResult.Succeeded();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new DataSourceConnectionTestResult(
                DataSourceConnectionTestStatus.Cancelled,
                "Connection test cancelled.");
        }
        catch (ArgumentException)
        {
            return new DataSourceConnectionTestResult(
                DataSourceConnectionTestStatus.InvalidConfiguration,
                "The SQL Server connection settings are invalid.");
        }
        catch (SqlException exception)
        {
            return Categorize(exception);
        }
    }

    private static SqlServerConnectionSettings ToSqlServerSettings(DataSourceConnectionRequest request)
    {
        if (request.Mode == DataSourceConnectionMode.Advanced)
        {
            return SqlServerConnectionSettings.CreateAdvanced(
                request.Secret ?? string.Empty,
                request.TrustServerCertificate);
        }

        _ = request.Properties.TryGetValue(DataSourceConnectionPropertyNames.Endpoint, out var server);
        _ = request.Properties.TryGetValue(DataSourceConnectionPropertyNames.Catalog, out var database);
        _ = request.Properties.TryGetValue(DataSourceConnectionPropertyNames.UserName, out var userName);
        _ = request.Properties.TryGetValue(DataSourceConnectionPropertyNames.IntegratedAuthentication, out var integratedSecurity);
        return SqlServerConnectionSettings.CreateStructured(
            server ?? string.Empty,
            database ?? string.Empty,
            bool.TryParse(integratedSecurity, out var integrated) && integrated,
            userName,
            request.Secret,
            request.TrustServerCertificate);
    }

    private static DataSourceConnectionTestResult Categorize(SqlException exception) =>
        exception.Number switch
        {
            18456 => new(
                DataSourceConnectionTestStatus.AuthenticationFailed,
                "SQL Server rejected the supplied credentials."),
            -2146893019 => new(
                DataSourceConnectionTestStatus.CertificateValidationFailed,
                "SQL Server certificate validation failed. Verify the server certificate before using the advanced trust override."),
            -2 or 53 or 11001 or 10060 => new(
                DataSourceConnectionTestStatus.NetworkFailure,
                "TurboBoard could not reach SQL Server. Verify the server address and network access."),
            4060 => new(
                DataSourceConnectionTestStatus.DatabaseUnavailable,
                "The selected database is unavailable to this login."),
            _ => new(
                DataSourceConnectionTestStatus.UnexpectedFailure,
                "SQL Server rejected the connection. Review the settings and server access."),
        };
}
