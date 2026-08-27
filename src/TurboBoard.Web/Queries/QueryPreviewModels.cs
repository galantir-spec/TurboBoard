using TurboBoard.Core.Queries;

namespace TurboBoard.Web.Queries;

public enum QueryPreviewStatus
{
    Succeeded,
    ValidationFailed,
    Failed,
}

public sealed record QueryPreviewResponse(
    QueryPreviewStatus Status,
    IReadOnlyList<ValidationDiagnostic> Diagnostics,
    string? GeneratedSql,
    DynamicResult? Result,
    string? FailureMessage = null);

public interface IQueryPreviewService
{
    Task<QueryPreviewResponse> PreviewAsync(
        Guid dataSourceId,
        QueryDefinition definition,
        IReadOnlyDictionary<string, string?>? parameterValues = null,
        CancellationToken cancellationToken = default);
}
