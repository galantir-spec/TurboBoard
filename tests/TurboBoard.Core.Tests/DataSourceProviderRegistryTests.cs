using TurboBoard.Core.DataSources;

namespace TurboBoard.Core.Tests;

public sealed class DataSourceProviderRegistryTests
{
    [Fact]
    public void A_provider_is_resolved_by_its_stable_key_without_case_sensitivity()
    {
        var tester = new StubConnectionTester("sql-server");
        var registry = new DataSourceProviderRegistry([tester]);

        var resolved = registry.GetConnectionTester("SQL-SERVER");

        Assert.Same(tester, resolved);
    }

    [Fact]
    public void An_unknown_provider_cannot_cross_the_registry_boundary()
    {
        var registry = new DataSourceProviderRegistry([]);

        var exception = Assert.Throws<KeyNotFoundException>(
            () => registry.GetConnectionTester("unknown"));

        Assert.Contains("unknown", exception.Message, StringComparison.Ordinal);
    }

    private sealed class StubConnectionTester(string providerKey) : IDataSourceConnectionTester
    {
        public string ProviderKey => providerKey;

        public Task<DataSourceConnectionTestResult> TestAsync(
            DataSourceConnectionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(DataSourceConnectionTestResult.Succeeded());
    }
}
