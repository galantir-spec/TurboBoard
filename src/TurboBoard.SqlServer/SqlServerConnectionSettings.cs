namespace TurboBoard.SqlServer;

public enum SqlServerConnectionMode
{
    Structured,
    Advanced,
}

public sealed record SqlServerConnectionSettings
{
    private SqlServerConnectionSettings()
    {
    }

    public SqlServerConnectionMode Mode { get; private init; }

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
            Mode = SqlServerConnectionMode.Structured,
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
            Mode = SqlServerConnectionMode.Advanced,
            ConnectionString = connectionString,
            TrustServerCertificate = trustServerCertificate,
        };
}
