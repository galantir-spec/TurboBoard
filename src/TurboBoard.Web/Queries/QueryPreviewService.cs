using TurboBoard.Core.DataSources;
using TurboBoard.Core.Queries;
using TurboBoard.Web.DataSources;
using TurboBoard.Web.Schemas;

namespace TurboBoard.Web.Queries;

internal sealed class QueryPreviewService(
    ISchemaService schemas,
    IDataSourceConnectionRequestResolver connections,
    DataSourceProviderRegistry providers,
    ILogger<QueryPreviewService> logger) : IQueryPreviewService
{
    private const int DefaultPreviewLimit = 100;
    private static readonly EventId PreviewFailed = new(2300, nameof(PreviewFailed));

    public async Task<QueryPreviewResponse> PreviewAsync(
        Guid dataSourceId,
        QueryDefinition definition,
        CancellationToken cancellationToken = default)
    {
        var schema = await schemas.GetAsync(dataSourceId, cancellationToken);
        var connection = await connections.ResolveAsync(dataSourceId, cancellationToken);
        if (schema is null || connection is null)
        {
            return new(
                QueryPreviewStatus.ValidationFailed,
                [new("query.schema.required", "Discover a current Schema before previewing this Query Definition.")],
                null,
                null);
        }

        var preparation = QueryEngine.Prepare(schema, definition);
        if (!preparation.IsValid)
        {
            return new(QueryPreviewStatus.ValidationFailed, preparation.Diagnostics, null, null);
        }

        var compiler = providers.GetQueryCompiler(connection.Request.ProviderKey);
        var compiled = compiler.Compile(preparation.Query!, DefaultPreviewLimit);
        try
        {
            var result = await providers.GetQueryExecutor(connection.Request.ProviderKey)
                .ExecuteAsync(connection.Request, compiled, cancellationToken);
            return new(QueryPreviewStatus.Succeeded, [], compiled.InspectionText, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(PreviewFailed, exception, "Query Preview failed for Data Source {DataSourceId}", dataSourceId);
            return new(
                QueryPreviewStatus.Failed,
                [],
                compiled.InspectionText,
                null,
                "TurboBoard could not execute this Query Preview.");
        }
    }
}
