using TurboBoard.Core.Schemas;

namespace TurboBoard.Core.Queries;

public sealed record DynamicResultColumn(
    int Ordinal,
    string Name,
    NormalizedTypeCategory NormalizedType,
    string ProviderType,
    bool IsNullable);

public sealed record QueryParameterSpecification(
    string Name,
    object Value,
    string ProviderType,
    int? MaximumLength,
    byte? Precision,
    byte? Scale);

public interface ICompiledQuery
{
    string InspectionText { get; }
    int PreviewLimit { get; }
    IReadOnlyList<DynamicResultColumn> Columns { get; }
    IReadOnlyList<QueryParameterSpecification> Parameters => [];
}

public sealed class QueryCompilationException(IReadOnlyList<ValidationDiagnostic> diagnostics)
    : Exception("The query could not be compiled safely.")
{
    public IReadOnlyList<ValidationDiagnostic> Diagnostics { get; } = diagnostics;
}

public interface IQueryCompiler
{
    string ProviderKey { get; }
    ICompiledQuery Compile(ExecutableQuery query, int previewLimit);
}
