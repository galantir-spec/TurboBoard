using TurboBoard.Core.DataSources;

namespace TurboBoard.SqlServer;

public sealed record SqlServerConnectionSettings
{
    private SqlServerConnectionSettings()
    {
    }

    public DataSourceConnectionMode Mode { get; private init; }

    public string? Server { get; private init; }

    public string? Database { get; private init; }

    public bool UseIntegratedSecurity { get; private init; }

    public string? UserName { get; private init; }

    public string? Password { get; private init; }

    public string? ConnectionString { get; private init; }

    public bool TrustServerCertificate { get; private init; }

    public static SqlServerConnectionSettings CreateStructured(
        string server,
        string database,
        bool useIntegratedSecurity,
        string? userName = null,
        string? password = null,
        bool trustServerCertificate = false) =>
        new()
        {
            Mode = DataSourceConnectionMode.Structured,
            Server = server,
            Database = database,
            UseIntegratedSecurity = useIntegratedSecurity,
            UserName = userName,
            Password = password,
            TrustServerCertificate = trustServerCertificate,
        };

    public static SqlServerConnectionSettings CreateAdvanced(
        string connectionString,
        bool trustServerCertificate = false) =>
        new()
        {
            Mode = DataSourceConnectionMode.Advanced,
            ConnectionString = connectionString,
            TrustServerCertificate = trustServerCertificate,
        };
}
