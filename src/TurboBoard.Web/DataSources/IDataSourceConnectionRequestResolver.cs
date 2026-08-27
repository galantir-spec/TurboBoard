using TurboBoard.Core.DataSources;

namespace TurboBoard.Web.DataSources;

internal sealed record DataSourceConnectionResolution(
    string Name,
    DataSourceConnectionRequest Request);

internal interface IDataSourceConnectionRequestResolver
{
    Task<DataSourceConnectionResolution?> ResolveAsync(
        Guid dataSourceId,
        CancellationToken cancellationToken = default);
}
