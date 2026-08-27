using TurboBoard.Core.Schemas;

namespace TurboBoard.Core.Queries;

public sealed record QuerySource(Guid Id, QualifiedDatabaseObjectName Object);

public sealed record QuerySelection(Guid SourceId, string ColumnName, string OutputName);

public enum QueryFilterOperator
{
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    Like,
    NotLike,
    Contains,
    StartsWith,
    EndsWith,
    In,
    NotIn,
    Between,
    IsNull,
    IsNotNull,
}

public sealed record QueryFilter(
    Guid SourceId,
    string ColumnName,
    QueryFilterOperator Operator,
    IReadOnlyList<string?> Values);

public sealed record QueryDefinition(
    int Version,
    QuerySource Source,
    IReadOnlyList<QuerySelection> Selections,
    IReadOnlyList<QueryFilter>? Filters = null)
{
    public const int CurrentVersion = 1;
    public IReadOnlyList<QueryFilter> AvailableFilters => Filters ?? [];
}

public sealed record ValidationDiagnostic(string Code, string Message);

public sealed record ExecutableSelection(SchemaColumn Column, string OutputName);

public sealed record ExecutableFilter(
    SchemaColumn Column,
    QueryFilterOperator Operator,
    IReadOnlyList<object> Values);

public sealed class ExecutableQuery
{
    internal ExecutableQuery(
        Guid sourceId,
        SchemaDatabaseObject source,
        IReadOnlyList<ExecutableSelection> selections,
        IReadOnlyList<ExecutableFilter> filters)
    {
        SourceId = sourceId;
        Source = source;
        Selections = selections;
        Filters = filters;
    }

    public Guid SourceId { get; }
    public SchemaDatabaseObject Source { get; }
    public IReadOnlyList<ExecutableSelection> Selections { get; }
    public IReadOnlyList<ExecutableFilter> Filters { get; }
}

public sealed record QueryPreparationResult(
    ExecutableQuery? Query,
    IReadOnlyList<ValidationDiagnostic> Diagnostics)
{
    public bool IsValid => Query is not null && Diagnostics.Count == 0;
}
