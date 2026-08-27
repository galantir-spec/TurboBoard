using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TurboBoard.Web.Tests;

public sealed class ApplicationShellTests
{
    [Fact]
    public async Task Analyst_can_open_the_shell_and_primary_destinations()
    {
        using var stateDirectory = TemporaryDirectory.Create();
        await using var application = new TurboBoardApplicationFactory(stateDirectory.Path);
        using var client = application.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        var overview = await client.GetAsync("/");
        var overviewHtml = await overview.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        Assert.Contains("TurboBoard", overviewHtml, StringComparison.Ordinal);
        Assert.Contains("href=\"data-sources\"", overviewHtml, StringComparison.Ordinal);
        Assert.Contains("href=\"queries\"", overviewHtml, StringComparison.Ordinal);

        var dataSources = await client.GetAsync("/data-sources");
        var dataSourcesHtml = await dataSources.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, dataSources.StatusCode);
        Assert.Contains("Data Sources", dataSourcesHtml, StringComparison.Ordinal);

        var queries = await client.GetAsync("/queries");
        var queriesHtml = await queries.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, queries.StatusCode);
        Assert.Contains("Queries", queriesHtml, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(stateDirectory.Path, "turboboard.db")));
        Assert.True(Directory.Exists(Path.Combine(stateDirectory.Path, "keys")));
    }

    [Fact]
    public async Task Startup_materializes_a_durable_data_protection_key()
    {
        using var stateDirectory = TemporaryDirectory.Create();
        await using var application = new TurboBoardApplicationFactory(stateDirectory.Path);
        using var client = application.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        Assert.NotEmpty(Directory.EnumerateFiles(
            Path.Combine(stateDirectory.Path, "keys"),
            "*.xml",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task Startup_wraps_database_initialization_failures_safely()
    {
        using var stateDirectory = TemporaryDirectory.Create();
        _ = Directory.CreateDirectory(Path.Combine(stateDirectory.Path, "turboboard.db"));
        await using var application = new TurboBoardApplicationFactory(stateDirectory.Path);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            application.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            }));

        Assert.StartsWith(
            "TurboBoard durable state could not be initialized",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_fails_safely_when_the_state_location_is_not_a_directory()
    {
        using var testDirectory = TemporaryDirectory.Create();
        var invalidStatePath = Path.Combine(testDirectory.Path, "state-file");
        await File.WriteAllTextAsync(invalidStatePath, "not a directory");
        await using var application = new TurboBoardApplicationFactory(invalidStatePath);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            application.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            }));

        Assert.Contains(
            "TurboBoard durable state could not be initialized",
            exception.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain("not a directory", exception.ToString(), StringComparison.Ordinal);
    }

    private sealed class TurboBoardApplicationFactory(string stateDirectory)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("TurboBoard:StateDirectory", stateDirectory);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            return new TemporaryDirectory(
                Directory.CreateTempSubdirectory("TurboBoardTests-").FullName);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
