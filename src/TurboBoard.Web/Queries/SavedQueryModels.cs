using TurboBoard.Core.Queries;

namespace TurboBoard.Web.Queries;

public sealed record SavedQueryDraft(
    Guid DataSourceId,
    string Name,
    string Description,
    QueryDefinition Definition);

public sealed record SavedQuerySummary(
    Guid Id,
    Guid DataSourceId,
    string Name,
    string Description,
    DateTimeOffset UpdatedAtUtc);

public sealed record SavedQueryDetails(
    Guid Id,
    Guid DataSourceId,
    string Name,
    string Description,
    QueryDefinition? Definition,
    ValidationDiagnostic? Diagnostic,
    DateTimeOffset UpdatedAtUtc);

public interface ISavedQueryService
{
    Task<IReadOnlyList<SavedQuerySummary>> ListAsync(Guid dataSourceId, CancellationToken cancellationToken = default);
    Task<SavedQueryDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Guid> SaveAsync(Guid? id, SavedQueryDraft draft, CancellationToken cancellationToken = default);
    Task UpdateMetadataAsync(Guid id, string name, string description, CancellationToken cancellationToken = default);
    Task<Guid> DuplicateAsync(Guid id, string name, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class SavedQueryValidationException(IReadOnlyList<string> diagnostics)
    : Exception("The Saved Query is not valid.")
{
    public IReadOnlyList<string> Diagnostics { get; } = diagnostics;
}
