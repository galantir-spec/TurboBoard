using TurboBoard.SqlServer;

namespace TurboBoard.Web.DataSources;

public interface IDataSourceService
{
    Task<IReadOnlyList<DataSourceSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<DataSourceDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Guid> SaveAsync(
        Guid? id,
        DataSourceDraft draft,
        CancellationToken cancellationToken = default);

    Task<SqlServerConnectionTestResult> TestAsync(
        Guid? id,
        DataSourceDraft draft,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
