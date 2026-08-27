using TurboBoard.Core.Schemas;

namespace TurboBoard.Core.Queries;

public sealed record DynamicResultColumn(
    int Ordinal,
    string Name,
    NormalizedTypeCategory NormalizedType,
    string ProviderType,
    bool IsNullable);

public interface ICompiledQuery
{
    string InspectionText { get; }
    int PreviewLimit { get; }
    IReadOnlyList<DynamicResultColumn> Columns { get; }
}

public interface IQueryCompiler
{
    string ProviderKey { get; }
    ICompiledQuery Compile(ExecutableQuery query, int previewLimit);
}
