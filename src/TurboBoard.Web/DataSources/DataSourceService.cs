using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TurboBoard.Core.DataSources;
using TurboBoard.Persistence;
using TurboBoard.Web.Schemas;

namespace TurboBoard.Web.DataSources;

internal sealed class DataSourceService : IDataSourceService, IDataSourceConnectionRequestResolver
{
    private const string ProviderKey = "sql-server";
    private const string ProtectionPurpose = "TurboBoard.DataSources.SqlServer.Settings.v1";
    private static readonly EventId ConnectionTestCompleted = new(2100, nameof(ConnectionTestCompleted));
    private static readonly EventId ConnectionTestFailed = new(2101, nameof(ConnectionTestFailed));

    private readonly IDbContextFactory<TurboBoardDbContext> contextFactory;
    private readonly DataSourceProviderRegistry providerRegistry;
    private readonly IDataProtector settingsProtector;
    private readonly ILogger<DataSourceService> logger;
    private readonly SchemaMemoryCache? schemaCache;

    public DataSourceService(
        IDbContextFactory<TurboBoardDbContext> contextFactory,
        DataSourceProviderRegistry providerRegistry,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<DataSourceService> logger,
        SchemaMemoryCache? schemaCache = null)
    {
        this.contextFactory = contextFactory;
        this.providerRegistry = providerRegistry;
        settingsProtector = dataProtectionProvider.CreateProtector(ProtectionPurpose);
        this.logger = logger;
        this.schemaCache = schemaCache;
    }

    public async Task<IReadOnlyList<DataSourceSummary>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var records = await context.DataSources
            .AsNoTracking()
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken);

        return records.Select(ToSummary).ToArray();
    }

    public async Task<DataSourceDetails?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.DataSources
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return record is null ? null : ToDetails(record);
    }

    public async Task<Guid> SaveAsync(
        Guid? id,
        DataSourceDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = id is null
            ? null
            : await context.DataSources.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("The Data Source no longer exists.");
        var settings = ResolveAndValidate(draft, existing);
        var now = DateTimeOffset.UtcNow;
        var record = existing ?? new DataSourceRecord
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = now,
        };

        record.Name = draft.Name.Trim();
        record.Description = draft.Description.Trim();
        record.Provider = ProviderKey;
        record.ProtectedSettings = Protect(settings);
        record.ConfigurationVersion = Guid.NewGuid();
        record.UpdatedAtUtc = now;
        if (existing is null)
        {
            context.DataSources.Add(record);
        }
        else
        {
            var schemaSnapshot = await context.SchemaSnapshots
                .SingleOrDefaultAsync(item => item.DataSourceId == record.Id, cancellationToken);
            if (schemaSnapshot is not null)
            {
                context.SchemaSnapshots.Remove(schemaSnapshot);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        schemaCache?.Remove(record.Id);
        return record.Id;
    }

    public async Task<DataSourceConnectionTestResult> TestAsync(
        Guid? id,
        DataSourceDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = id is null
            ? null
            : await context.DataSources
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("The Data Source no longer exists.");
        var settings = ResolveAndValidate(draft, existing);

        try
        {
            var connectionTester = providerRegistry.GetConnectionTester(ProviderKey);
            var result = await connectionTester.TestAsync(ToConnectionRequest(settings), cancellationToken);
            logger.LogInformation(
                ConnectionTestCompleted,
                "Data Source connection test completed with status {ConnectionTestStatus} for {DataSourceId}",
                result.Status,
                id);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new DataSourceConnectionTestResult(
                DataSourceConnectionTestStatus.Cancelled,
                "Connection test cancelled.");
        }
        catch (Exception)
        {
            logger.LogWarning(
                ConnectionTestFailed,
                "Data Source connection test failed unexpectedly for {DataSourceId}",
                id);
            return new DataSourceConnectionTestResult(
                DataSourceConnectionTestStatus.UnexpectedFailure,
                "TurboBoard could not test this Data Source. Review the settings and try again.");
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.DataSources.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (record is null)
        {
            return false;
        }

        context.DataSources.Remove(record);
        await context.SaveChangesAsync(cancellationToken);
        schemaCache?.Remove(id);
        return true;
    }

    async Task<DataSourceConnectionResolution?> IDataSourceConnectionRequestResolver.ResolveAsync(
        Guid dataSourceId,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.DataSources
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == dataSourceId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        return new DataSourceConnectionResolution(
            record.Name,
            record.ConfigurationVersion,
            ToConnectionRequest(Unprotect(record.ProtectedSettings)));
    }

    private DataSourceSummary ToSummary(DataSourceRecord record)
    {
        var settings = Unprotect(record.ProtectedSettings);
        var target = settings.Mode == DataSourceConnectionMode.Structured
            ? $"{settings.Server} / {settings.Database}"
            : "Advanced connection string";
        return new DataSourceSummary(
            record.Id,
            record.Name,
            record.Description,
            settings.Mode,
            target,
            settings.TrustServerCertificate,
            record.UpdatedAtUtc);
    }

    private DataSourceDetails ToDetails(DataSourceRecord record)
    {
        var settings = Unprotect(record.ProtectedSettings);
        return new DataSourceDetails(
            record.Id,
            record.Name,
            record.Description,
            settings.Mode,
            settings.Server ?? string.Empty,
            settings.Database ?? string.Empty,
            settings.UseIntegratedSecurity,
            settings.UserName ?? string.Empty,
            settings.TrustServerCertificate,
            HasSecret(settings));
    }

    private StoredSqlServerSettings ResolveAndValidate(
        DataSourceDraft draft,
        DataSourceRecord? existing)
    {
        var diagnostics = new List<string>();
        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            diagnostics.Add("Enter a Data Source name.");
        }
        else if (draft.Name.Trim().Length > 200)
        {
            diagnostics.Add("The Data Source name must be 200 characters or fewer.");
        }

        if (draft.Description.Trim().Length > 2000)
        {
            diagnostics.Add("The description must be 2,000 characters or fewer.");
        }

        var previous = existing is null ? null : Unprotect(existing.ProtectedSettings);
        var settings = draft.Mode switch
        {
            DataSourceConnectionMode.Structured => ResolveStructured(draft, previous, diagnostics),
            DataSourceConnectionMode.Advanced => ResolveAdvanced(draft, previous, diagnostics),
            _ => throw new DataSourceValidationException(["Choose a supported connection mode."]),
        };

        if (diagnostics.Count > 0)
        {
            throw new DataSourceValidationException(diagnostics);
        }

        return settings;
    }

    private static StoredSqlServerSettings ResolveStructured(
        DataSourceDraft draft,
        StoredSqlServerSettings? previous,
        ICollection<string> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(draft.Server))
        {
            diagnostics.Add("Enter the SQL Server host or instance.");
        }

        if (string.IsNullOrWhiteSpace(draft.Database))
        {
            diagnostics.Add("Enter the database name.");
        }

        var password = draft.UseIntegratedSecurity
            ? null
            : string.IsNullOrEmpty(draft.Password) && previous?.Mode == DataSourceConnectionMode.Structured
                ? previous.Password
                : draft.Password;
        if (!draft.UseIntegratedSecurity && string.IsNullOrWhiteSpace(draft.UserName))
        {
            diagnostics.Add("Enter the SQL Server login name.");
        }

        if (!draft.UseIntegratedSecurity && string.IsNullOrEmpty(password))
        {
            diagnostics.Add("Enter a password. Leave it empty only when retaining an existing password.");
        }

        return new StoredSqlServerSettings
        {
            Mode = DataSourceConnectionMode.Structured,
            Server = draft.Server.Trim(),
            Database = draft.Database.Trim(),
            UseIntegratedSecurity = draft.UseIntegratedSecurity,
            UserName = draft.UseIntegratedSecurity ? null : draft.UserName.Trim(),
            Password = password,
            TrustServerCertificate = draft.TrustServerCertificate,
        };
    }

    private static StoredSqlServerSettings ResolveAdvanced(
        DataSourceDraft draft,
        StoredSqlServerSettings? previous,
        ICollection<string> diagnostics)
    {
        var connectionString = string.IsNullOrEmpty(draft.ConnectionString)
            && previous?.Mode == DataSourceConnectionMode.Advanced
                ? previous.ConnectionString
                : draft.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            diagnostics.Add("Enter an advanced connection string. Leave it empty only when retaining an existing value.");
        }
        return new StoredSqlServerSettings
        {
            Mode = DataSourceConnectionMode.Advanced,
            ConnectionString = connectionString,
            TrustServerCertificate = draft.TrustServerCertificate,
        };
    }

    private string Protect(StoredSqlServerSettings settings) =>
        settingsProtector.Protect(JsonSerializer.Serialize(settings));

    private StoredSqlServerSettings Unprotect(string protectedSettings)
    {
        try
        {
            var json = settingsProtector.Unprotect(protectedSettings);
            return JsonSerializer.Deserialize<StoredSqlServerSettings>(json)
                ?? throw new InvalidOperationException();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "The protected Data Source settings could not be read. Restore the matching application key ring.",
                exception);
        }
    }

    private static DataSourceConnectionRequest ToConnectionRequest(StoredSqlServerSettings settings) =>
        new(
            ProviderKey,
            settings.Mode,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                [DataSourceConnectionPropertyNames.Endpoint] = settings.Server,
                [DataSourceConnectionPropertyNames.Catalog] = settings.Database,
                [DataSourceConnectionPropertyNames.IntegratedAuthentication] = settings.UseIntegratedSecurity.ToString(),
                [DataSourceConnectionPropertyNames.UserName] = settings.UserName,
            },
            settings.Mode == DataSourceConnectionMode.Advanced
                ? settings.ConnectionString
                : settings.Password,
            settings.TrustServerCertificate);

    private static bool HasSecret(StoredSqlServerSettings settings) =>
        settings.Mode == DataSourceConnectionMode.Advanced
            ? !string.IsNullOrEmpty(settings.ConnectionString)
            : !settings.UseIntegratedSecurity && !string.IsNullOrEmpty(settings.Password);

    private sealed class StoredSqlServerSettings
    {
        public DataSourceConnectionMode Mode { get; init; }

        public string? Server { get; init; }

        public string? Database { get; init; }

        public bool UseIntegratedSecurity { get; init; }

        public string? UserName { get; init; }

        public string? Password { get; init; }

        public string? ConnectionString { get; init; }

        public bool TrustServerCertificate { get; init; }
    }
}
