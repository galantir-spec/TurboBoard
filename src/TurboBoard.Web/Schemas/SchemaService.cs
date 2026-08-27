using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TurboBoard.Core.DataSources;
using TurboBoard.Core.Schemas;
using TurboBoard.Persistence;
using TurboBoard.Web.DataSources;

namespace TurboBoard.Web.Schemas;

internal sealed class SchemaService(
    IDbContextFactory<TurboBoardDbContext> contextFactory,
    IDataSourceConnectionRequestResolver connectionResolver,
    DataSourceProviderRegistry providerRegistry,
    SchemaMemoryCache cache,
    SchemaRefreshCoordinator coordinator,
    ILogger<SchemaService> logger) : ISchemaService
{
    private static readonly EventId DiscoveryFailed = new(2200, nameof(DiscoveryFailed));

    public async Task<DataSourceSchema?> GetAsync(
        Guid dataSourceId,
        CancellationToken cancellationToken = default)
    {
        if (cache.TryGet(dataSourceId, out var cached))
        {
            if (await IsCurrentConfigurationAsync(
                dataSourceId,
                cached!.ConfigurationVersion,
                cancellationToken))
            {
                return cached.Schema;
            }

            cache.Remove(dataSourceId);
            return null;
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.SchemaSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.DataSourceId == dataSourceId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        var schema = JsonSerializer.Deserialize<DataSourceSchema>(record.SchemaJson)
            ?? throw new InvalidOperationException("The persisted Schema snapshot is invalid.");
        cache.Set(schema, record.ConfigurationVersion);
        if (!await IsCurrentConfigurationAsync(dataSourceId, record.ConfigurationVersion, cancellationToken))
        {
            cache.Remove(dataSourceId);
            return null;
        }

        return schema;
    }

    public async Task<SchemaState?> GetStateAsync(
        Guid dataSourceId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.SchemaSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.DataSourceId == dataSourceId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        var schema = cache.TryGet(dataSourceId, out var cached) &&
            cached!.ConfigurationVersion == record.ConfigurationVersion
            ? cached.Schema
            : JsonSerializer.Deserialize<DataSourceSchema>(record.SchemaJson)
                ?? throw new InvalidOperationException("The persisted Schema snapshot is invalid.");
        cache.Set(schema, record.ConfigurationVersion);
        if (!await IsCurrentConfigurationAsync(dataSourceId, record.ConfigurationVersion, cancellationToken))
        {
            cache.Remove(dataSourceId);
            return null;
        }

        return new SchemaState(
            schema,
            Enum.TryParse<SchemaDiscoveryStatus>(record.LastRefreshFailureStatus, out var failureStatus)
                ? failureStatus
                : null,
            record.LastRefreshFailureMessage,
            record.LastRefreshAttemptedAtUtc);
    }

    public Task<SchemaRefreshResult> RefreshAsync(
        Guid dataSourceId,
        CancellationToken cancellationToken = default) =>
        coordinator.RunAsync(
            dataSourceId,
            operationCancellationToken => RefreshCoreAsync(dataSourceId, operationCancellationToken),
            cancellationToken);

    private async Task<SchemaRefreshResult> RefreshCoreAsync(
        Guid dataSourceId,
        CancellationToken cancellationToken)
    {
        var resolution = await connectionResolver.ResolveAsync(dataSourceId, cancellationToken);
        if (resolution is null)
        {
            return new SchemaRefreshResult(
                SchemaRefreshStatus.Failed,
                "That Data Source no longer exists.",
                null,
                SchemaDiscoveryStatus.InvalidConfiguration);
        }

        SchemaDiscoveryResult discovery;
        try
        {
            discovery = await providerRegistry
                .GetSchemaDiscoverer(resolution.Request.ProviderKey)
                .DiscoverAsync(resolution.Request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            discovery = new SchemaDiscoveryResult(
                SchemaDiscoveryStatus.Cancelled,
                "Schema discovery cancelled.");
        }
        catch (Exception)
        {
            logger.LogWarning(
                DiscoveryFailed,
                "Schema discovery failed unexpectedly for Data Source {DataSourceId}",
                dataSourceId);
            discovery = new SchemaDiscoveryResult(
                SchemaDiscoveryStatus.UnexpectedFailure,
                "TurboBoard could not discover this Schema. Try again later.");
        }

        if (discovery.Status != SchemaDiscoveryStatus.Succeeded || discovery.Objects is null)
        {
            await RecordFailureAsync(
                dataSourceId,
                resolution.ConfigurationVersion,
                discovery,
                cancellationToken);
            return new SchemaRefreshResult(
                SchemaRefreshStatus.Failed,
                discovery.Message,
                await GetAsync(dataSourceId, cancellationToken),
                discovery.Status);
        }

        var schema = new DataSourceSchema(dataSourceId, DateTimeOffset.UtcNow, discovery.Objects);
        if (!await PersistIfCurrentAsync(schema, resolution.ConfigurationVersion, cancellationToken))
        {
            return new SchemaRefreshResult(
                SchemaRefreshStatus.Failed,
                "The Data Source settings changed during discovery. Refresh the Schema again.",
                await GetAsync(dataSourceId, cancellationToken),
                SchemaDiscoveryStatus.InvalidConfiguration);
        }

        return new SchemaRefreshResult(
            SchemaRefreshStatus.Succeeded,
            $"Discovered {schema.Objects.Count} database objects.",
            schema);
    }

    private async Task<bool> PersistIfCurrentAsync(
        DataSourceSchema schema,
        Guid configurationVersion,
        CancellationToken cancellationToken)
    {
        if (!await IsCurrentConfigurationAsync(schema.DataSourceId, configurationVersion, cancellationToken))
        {
            return false;
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.SchemaSnapshots
            .SingleOrDefaultAsync(item => item.DataSourceId == schema.DataSourceId, cancellationToken);
        if (record is null)
        {
            record = new SchemaSnapshotRecord { DataSourceId = schema.DataSourceId };
            context.SchemaSnapshots.Add(record);
        }

        record.ConfigurationVersion = configurationVersion;
        record.SchemaJson = JsonSerializer.Serialize(schema);
        record.DiscoveredAtUtc = schema.DiscoveredAtUtc;
        record.LastRefreshFailureStatus = null;
        record.LastRefreshFailureMessage = null;
        record.LastRefreshAttemptedAtUtc = null;
        await context.SaveChangesAsync(cancellationToken);
        cache.Set(schema, configurationVersion);
        if (await IsCurrentConfigurationAsync(schema.DataSourceId, configurationVersion, cancellationToken))
        {
            return true;
        }

        await RemoveIfVersionAsync(schema.DataSourceId, configurationVersion, cancellationToken);
        return false;
    }

    private async Task RecordFailureAsync(
        Guid dataSourceId,
        Guid configurationVersion,
        SchemaDiscoveryResult discovery,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.SchemaSnapshots
            .SingleOrDefaultAsync(
                item => item.DataSourceId == dataSourceId &&
                    item.ConfigurationVersion == configurationVersion,
                cancellationToken);
        if (record is null)
        {
            return;
        }

        record.LastRefreshFailureStatus = discovery.Status.ToString();
        record.LastRefreshFailureMessage = discovery.Message;
        record.LastRefreshAttemptedAtUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> IsCurrentConfigurationAsync(
        Guid dataSourceId,
        Guid configurationVersion,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.DataSources
            .AsNoTracking()
            .AnyAsync(
                item => item.Id == dataSourceId && item.ConfigurationVersion == configurationVersion,
                cancellationToken);
    }

    private async Task RemoveIfVersionAsync(
        Guid dataSourceId,
        Guid configurationVersion,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        _ = await context.SchemaSnapshots
            .Where(item => item.DataSourceId == dataSourceId && item.ConfigurationVersion == configurationVersion)
            .ExecuteDeleteAsync(cancellationToken);
        cache.Remove(dataSourceId);
    }
}
