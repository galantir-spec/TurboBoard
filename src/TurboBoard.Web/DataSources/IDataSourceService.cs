using TurboBoard.Core.DataSources;

namespace TurboBoard.Web.DataSources;

public interface IDataSourceService
{
    Task<IReadOnlyList<DataSourceSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<DataSourceDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Guid> SaveAsync(
        Guid? id,
        DataSourceDraft draft,
        CancellationToken cancellationToken = default);

    Task<DataSourceConnectionTestResult> TestAsync(
        Guid? id,
        DataSourceDraft draft,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
