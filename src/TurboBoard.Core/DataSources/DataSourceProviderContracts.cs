using TurboBoard.Core.Schemas;

namespace TurboBoard.Core.DataSources;

public enum DataSourceConnectionMode
{
    Structured,
    Advanced,
}

public enum DataSourceConnectionTestStatus
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

public sealed record DataSourceConnectionTestResult(
    DataSourceConnectionTestStatus Status,
    string Message)
{
    public static DataSourceConnectionTestResult Succeeded() =>
        new(DataSourceConnectionTestStatus.Succeeded, "Connection succeeded.");
}

public sealed record DataSourceConnectionRequest(
    string ProviderKey,
    DataSourceConnectionMode Mode,
    IReadOnlyDictionary<string, string?> Properties,
    string? Secret,
    bool TrustServerCertificate);

public static class DataSourceConnectionPropertyNames
{
    public const string Endpoint = "endpoint";
    public const string Catalog = "catalog";
    public const string IntegratedAuthentication = "integrated-authentication";
    public const string UserName = "user-name";
}

public interface IDataSourceConnectionTester
{
    string ProviderKey { get; }

    Task<DataSourceConnectionTestResult> TestAsync(
        DataSourceConnectionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class DataSourceProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IDataSourceConnectionTester> connectionTesters;
    private readonly IReadOnlyDictionary<string, IDataSourceSchemaDiscoverer> schemaDiscoverers;

    public DataSourceProviderRegistry(
        IEnumerable<IDataSourceConnectionTester> connectionTesters,
        IEnumerable<IDataSourceSchemaDiscoverer>? schemaDiscoverers = null)
    {
        ArgumentNullException.ThrowIfNull(connectionTesters);
        this.connectionTesters = connectionTesters.ToDictionary(
            tester => tester.ProviderKey,
            StringComparer.OrdinalIgnoreCase);
        this.schemaDiscoverers = (schemaDiscoverers ?? [])
            .ToDictionary(discoverer => discoverer.ProviderKey, StringComparer.OrdinalIgnoreCase);
    }

    public IDataSourceConnectionTester GetConnectionTester(string providerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        return connectionTesters.TryGetValue(providerKey, out var tester)
            ? tester
            : throw new KeyNotFoundException($"No connection tester is registered for provider '{providerKey}'.");
    }

    public IDataSourceSchemaDiscoverer GetSchemaDiscoverer(string providerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        return schemaDiscoverers.TryGetValue(providerKey, out var discoverer)
            ? discoverer
            : throw new KeyNotFoundException($"No Schema discoverer is registered for provider '{providerKey}'.");
    }
}
