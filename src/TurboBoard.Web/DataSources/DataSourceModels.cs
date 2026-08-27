using TurboBoard.SqlServer;

namespace TurboBoard.Web.DataSources;

public sealed class DataSourceDraft
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public SqlServerConnectionMode Mode { get; set; } = SqlServerConnectionMode.Structured;

    public string Server { get; set; } = string.Empty;

    public string Database { get; set; } = string.Empty;

    public bool UseIntegratedSecurity { get; set; } = true;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string ConnectionString { get; set; } = string.Empty;

    public bool TrustServerCertificate { get; set; }

    public static DataSourceDraft Structured(
        string name,
        string server,
        string database,
        bool useIntegratedSecurity,
        string? userName = null,
        string? password = null) =>
        new()
        {
            Name = name,
            Server = server,
            Database = database,
            UseIntegratedSecurity = useIntegratedSecurity,
            UserName = userName ?? string.Empty,
            Password = password ?? string.Empty,
        };
}

public sealed record DataSourceSummary(
    Guid Id,
    string Name,
    string Description,
    SqlServerConnectionMode Mode,
    string Target,
    bool TrustServerCertificate,
    DateTimeOffset UpdatedAtUtc);

public sealed record DataSourceDetails(
    Guid Id,
    string Name,
    string Description,
    SqlServerConnectionMode Mode,
    string Server,
    string Database,
    bool UseIntegratedSecurity,
    string UserName,
    bool TrustServerCertificate,
    bool HasStoredSecret);

public sealed class DataSourceValidationException(IReadOnlyList<string> diagnostics)
    : Exception("The Data Source settings are not valid.")
{
    public IReadOnlyList<string> Diagnostics { get; } = diagnostics;
}
