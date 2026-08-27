using Microsoft.Data.SqlClient;

namespace TurboBoard.SqlServer;

public interface ISqlServerConnectionTester
{
    Task<SqlServerConnectionTestResult> TestAsync(
        SqlServerConnectionSettings settings,
        CancellationToken cancellationToken = default);
}

public sealed class SqlServerConnectionTester : ISqlServerConnectionTester
{
    public async Task<SqlServerConnectionTestResult> TestAsync(
        SqlServerConnectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var connection = new SqlConnection(SqlServerConnectionString.Create(settings));
            await connection.OpenAsync(cancellationToken);
            return SqlServerConnectionTestResult.Succeeded();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new SqlServerConnectionTestResult(
                SqlServerConnectionTestStatus.Cancelled,
                "Connection test cancelled.");
        }
        catch (ArgumentException)
        {
            return new SqlServerConnectionTestResult(
                SqlServerConnectionTestStatus.InvalidConfiguration,
                "The SQL Server connection settings are invalid.");
        }
        catch (SqlException exception)
        {
            return Categorize(exception);
        }
    }

    private static SqlServerConnectionTestResult Categorize(SqlException exception) =>
        exception.Number switch
        {
            18456 => new(
                SqlServerConnectionTestStatus.AuthenticationFailed,
                "SQL Server rejected the supplied credentials."),
            -2146893019 => new(
                SqlServerConnectionTestStatus.CertificateValidationFailed,
                "SQL Server certificate validation failed. Verify the server certificate before using the advanced trust override."),
            -2 or 53 or 11001 or 10060 => new(
                SqlServerConnectionTestStatus.NetworkFailure,
                "TurboBoard could not reach SQL Server. Verify the server address and network access."),
            4060 => new(
                SqlServerConnectionTestStatus.DatabaseUnavailable,
                "The selected database is unavailable to this login."),
            _ => new(
                SqlServerConnectionTestStatus.UnexpectedFailure,
                "SQL Server rejected the connection. Review the settings and server access."),
        };
}

public enum SqlServerConnectionTestStatus
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

public sealed record SqlServerConnectionTestResult(
    SqlServerConnectionTestStatus Status,
    string Message)
{
    public static SqlServerConnectionTestResult Succeeded() =>
        new(SqlServerConnectionTestStatus.Succeeded, "Connection succeeded.");
}
