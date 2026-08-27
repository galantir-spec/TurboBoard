using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TurboBoard.Persistence;
using TurboBoard.SqlServer;
using TurboBoard.Web.DataSources;

namespace TurboBoard.Web.Tests;

public sealed class DataSourceLifecycleTests
{
    [Fact]
    public async Task Saving_a_data_source_encrypts_all_connection_settings_and_returns_no_secret()
    {
        await using var host = await DataSourceTestHost.CreateAsync();
        var draft = DataSourceDraft.Structured(
            name: "Revenue warehouse",
            server: "private-sql.internal",
            database: "revenue",
            useIntegratedSecurity: false,
            userName: "report_reader",
            password: "correct horse battery staple");

        var id = await host.WithServiceAsync(service => service.SaveAsync(null, draft));
        var details = await host.WithServiceAsync(service => service.GetAsync(id));
        var persistedPayload = await host.ReadProtectedPayloadAsync(id);

        Assert.NotNull(details);
        Assert.DoesNotContain("private-sql.internal", persistedPayload, StringComparison.Ordinal);
        Assert.DoesNotContain("report_reader", persistedPayload, StringComparison.Ordinal);
        Assert.DoesNotContain("correct horse battery staple", persistedPayload, StringComparison.Ordinal);
        Assert.DoesNotContain("correct horse battery staple", JsonSerializer.Serialize(details), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_password_during_edit_preserves_the_saved_secret()
    {
        var tester = new RecordingConnectionTester();
        await using var host = await DataSourceTestHost.CreateAsync(tester);
        var id = await host.WithServiceAsync(service => service.SaveAsync(
            null,
            DataSourceDraft.Structured(
                name: "Revenue warehouse",
                server: "sql.internal",
                database: "revenue",
                useIntegratedSecurity: false,
                userName: "report_reader",
                password: "keep-me")));
        var edit = DataSourceDraft.Structured(
            name: "Revenue warehouse renamed",
            server: "sql.internal",
            database: "revenue",
            useIntegratedSecurity: false,
            userName: "report_reader",
            password: string.Empty);

        await host.WithServiceAsync(service => service.SaveAsync(id, edit));
        await host.WithServiceAsync(service => service.TestAsync(id, edit));

        Assert.Equal("keep-me", tester.LastSettings?.Password);
    }

    [Fact]
    public async Task Testing_an_unsaved_data_source_uses_the_provider_without_persisting_it()
    {
        var tester = new RecordingConnectionTester();
        await using var host = await DataSourceTestHost.CreateAsync(tester);
        var draft = DataSourceDraft.Structured(
            name: "Candidate",
            server: "sql.internal",
            database: "analytics",
            useIntegratedSecurity: true);

        var result = await host.WithServiceAsync(service => service.TestAsync(null, draft));
        var saved = await host.WithServiceAsync(service => service.ListAsync());

        Assert.Equal(SqlServerConnectionTestStatus.Succeeded, result.Status);
        Assert.Equal("sql.internal", tester.LastSettings?.Server);
        Assert.Empty(saved);
    }

    [Fact]
    public async Task Unexpected_connection_failures_are_sanitized_and_secret_free_in_logs()
    {
        var tester = new ThrowingConnectionTester(
            "Server=private-sql;User ID=admin;Password=do-not-log");
        var logs = new RecordingLoggerProvider();
        await using var host = await DataSourceTestHost.CreateAsync(tester, logs);
        var draft = DataSourceDraft.Structured(
            name: "Candidate",
            server: "private-sql",
            database: "analytics",
            useIntegratedSecurity: false,
            userName: "admin",
            password: "do-not-log");

        var result = await host.WithServiceAsync(service => service.TestAsync(null, draft));

        Assert.Equal(SqlServerConnectionTestStatus.UnexpectedFailure, result.Status);
        Assert.Equal("TurboBoard could not test this Data Source. Review the settings and try again.", result.Message);
        Assert.DoesNotContain("do-not-log", logs.MessagesText, StringComparison.Ordinal);
        Assert.DoesNotContain("private-sql", logs.MessagesText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_a_data_source_removes_its_protected_configuration()
    {
        await using var host = await DataSourceTestHost.CreateAsync();
        var id = await host.WithServiceAsync(service => service.SaveAsync(
            null,
            DataSourceDraft.Structured(
                name: "Temporary",
                server: "sql.internal",
                database: "analytics",
                useIntegratedSecurity: true)));

        var deleted = await host.WithServiceAsync(service => service.DeleteAsync(id));
        var remaining = await host.WithServiceAsync(service => service.ListAsync());

        Assert.True(deleted);
        Assert.Empty(remaining);
    }

    private sealed class RecordingConnectionTester : ISqlServerConnectionTester
    {
        public SqlServerConnectionSettings? LastSettings { get; private set; }

        public Task<SqlServerConnectionTestResult> TestAsync(
            SqlServerConnectionSettings settings,
            CancellationToken cancellationToken = default)
        {
            LastSettings = settings;
            return Task.FromResult(SqlServerConnectionTestResult.Succeeded());
        }
    }

    private sealed class ThrowingConnectionTester(string message) : ISqlServerConnectionTester
    {
        public Task<SqlServerConnectionTestResult> TestAsync(
            SqlServerConnectionSettings settings,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(message);
    }

    private sealed class DataSourceTestHost : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        private readonly string databasePath;
        private readonly string stateDirectory;

        private DataSourceTestHost(
            ServiceProvider services,
            string databasePath,
            string stateDirectory)
        {
            this.services = services;
            this.databasePath = databasePath;
            this.stateDirectory = stateDirectory;
        }

        public static async Task<DataSourceTestHost> CreateAsync(
            ISqlServerConnectionTester? tester = null,
            ILoggerProvider? loggerProvider = null)
        {
            var stateDirectory = Directory.CreateTempSubdirectory("TurboBoardDataSources-").FullName;
            var keyDirectory = Directory.CreateDirectory(Path.Combine(stateDirectory, "keys"));
            var databasePath = Path.Combine(stateDirectory, "turboboard.db");
            var serviceCollection = new ServiceCollection();
            serviceCollection
                .AddDataProtection()
                .SetApplicationName("TurboBoard.Tests")
                .PersistKeysToFileSystem(keyDirectory);
            serviceCollection.AddLogging(builder =>
            {
                if (loggerProvider is not null)
                {
                    builder.AddProvider(loggerProvider);
                }
            });
            serviceCollection.AddTurboBoardPersistence(databasePath);
            serviceCollection.AddSingleton(tester ?? new RecordingConnectionTester());
            serviceCollection.AddTurboBoardDataSources();

            var services = serviceCollection.BuildServiceProvider();
            await services.InitializeTurboBoardPersistenceAsync();
            return new DataSourceTestHost(services, databasePath, stateDirectory);
        }

        public async Task<T> WithServiceAsync<T>(Func<IDataSourceService, Task<T>> action)
        {
            await using var scope = services.CreateAsyncScope();
            return await action(scope.ServiceProvider.GetRequiredService<IDataSourceService>());
        }

        public async Task WithServiceAsync(Func<IDataSourceService, Task> action)
        {
            await using var scope = services.CreateAsyncScope();
            await action(scope.ServiceProvider.GetRequiredService<IDataSourceService>());
        }

        public async Task<string> ReadProtectedPayloadAsync(Guid id)
        {
            await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT ProtectedSettings FROM DataSources WHERE Id = $id";
            _ = command.Parameters.AddWithValue("$id", id.ToString());
            return (string)(await command.ExecuteScalarAsync() ?? string.Empty);
        }

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> messages = [];

        public string MessagesText => string.Join(Environment.NewLine, messages);

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(messages);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                messages.Add(formatter(state, exception));
                if (exception is not null)
                {
                    messages.Add(exception.ToString());
                }
            }
        }
    }
}
