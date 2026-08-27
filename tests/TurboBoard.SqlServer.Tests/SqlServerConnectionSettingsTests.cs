using Microsoft.Data.SqlClient;
using TurboBoard.Core.DataSources;

namespace TurboBoard.SqlServer.Tests;

public sealed class SqlServerConnectionSettingsTests
{
    [Fact]
    public void Structured_settings_require_encrypted_certificate_validated_transport_by_default()
    {
        var settings = SqlServerConnectionSettings.CreateStructured(
            server: "sql.internal",
            database: "analytics",
            useIntegratedSecurity: false,
            userName: "analyst",
            password: "secret");

        var connectionString = SqlServerConnectionString.Create(settings);
        var parsed = new SqlConnectionStringBuilder(connectionString);

        Assert.Equal(SqlConnectionEncryptOption.Mandatory, parsed.Encrypt);
        Assert.False(parsed.TrustServerCertificate);
        Assert.Equal("sql.internal", parsed.DataSource);
        Assert.Equal("analytics", parsed.InitialCatalog);
    }

    [Fact]
    public void Advanced_settings_enforce_transport_policy_and_apply_an_explicit_trust_override()
    {
        var settings = SqlServerConnectionSettings.CreateAdvanced(
            "Server=sql.internal;Database=analytics;User ID=analyst;Password=secret;Encrypt=False;TrustServerCertificate=False",
            trustServerCertificate: true);

        var connectionString = SqlServerConnectionString.Create(settings);
        var parsed = new SqlConnectionStringBuilder(connectionString);

        Assert.Equal(SqlConnectionEncryptOption.Mandatory, parsed.Encrypt);
        Assert.True(parsed.TrustServerCertificate);
    }

    [Fact]
    public async Task A_cancelled_connection_test_is_reported_without_opening_a_connection()
    {
        var tester = new SqlServerConnectionTester();
        var settings = SqlServerConnectionSettings.CreateStructured(
            server: "sql.invalid",
            database: "analytics",
            useIntegratedSecurity: true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await tester.TestAsync(settings, cancellation.Token);

        Assert.Equal(DataSourceConnectionTestStatus.Cancelled, result.Status);
        Assert.Equal("Connection test cancelled.", result.Message);
    }
}
