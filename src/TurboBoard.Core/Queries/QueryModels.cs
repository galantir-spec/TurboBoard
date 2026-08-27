using System.Text.Json.Serialization;
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

public enum QueryFilterGroupOperator
{
    And,
    Or,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(QueryFilterCondition), "condition")]
[JsonDerivedType(typeof(QueryFilterGroup), "group")]
[JsonDerivedType(typeof(QueryFilterNot), "not")]
public abstract record QueryFilterExpression(Guid Id, bool IsEnabled);

public sealed record QueryFilterCondition(Guid Id, bool IsEnabled, QueryFilter Filter)
    : QueryFilterExpression(Id, IsEnabled);

public sealed record QueryFilterGroup(
    Guid Id,
    bool IsEnabled,
    QueryFilterGroupOperator Operator,
    IReadOnlyList<QueryFilterExpression> Children)
    : QueryFilterExpression(Id, IsEnabled);

public sealed record QueryFilterNot(Guid Id, bool IsEnabled, QueryFilterExpression Operand)
    : QueryFilterExpression(Id, IsEnabled);

public sealed record QueryDefinition(
    int Version,
    QuerySource Source,
    IReadOnlyList<QuerySelection> Selections,
    IReadOnlyList<QueryFilter>? Filters = null,
    QueryFilterExpression? FilterExpression = null)
{
    public const int CurrentVersion = 2;
    public IReadOnlyList<QueryFilter> AvailableFilters => Filters ?? [];
}

public sealed record ValidationDiagnostic(string Code, string Message);

public sealed record ExecutableSelection(SchemaColumn Column, string OutputName);

public sealed record ExecutableFilter(
    SchemaColumn Column,
    QueryFilterOperator Operator,
    IReadOnlyList<object> Values);

public abstract record ExecutableFilterExpression;
public sealed record ExecutableFilterCondition(ExecutableFilter Filter) : ExecutableFilterExpression;
public sealed record ExecutableFilterGroup(QueryFilterGroupOperator Operator, IReadOnlyList<ExecutableFilterExpression> Children) : ExecutableFilterExpression;
public sealed record ExecutableFilterNot(ExecutableFilterExpression Operand) : ExecutableFilterExpression;

public sealed class ExecutableQuery
{
    internal ExecutableQuery(
        Guid sourceId,
        SchemaDatabaseObject source,
        IReadOnlyList<ExecutableSelection> selections,
        IReadOnlyList<ExecutableFilter> filters,
        ExecutableFilterExpression? filterExpression)
    {
        SourceId = sourceId;
        Source = source;
        Selections = selections;
        Filters = filters;
        FilterExpression = filterExpression;
    }

    public Guid SourceId { get; }
    public SchemaDatabaseObject Source { get; }
    public IReadOnlyList<ExecutableSelection> Selections { get; }
    public IReadOnlyList<ExecutableFilter> Filters { get; }
    public ExecutableFilterExpression? FilterExpression { get; }
}

public sealed record QueryPreparationResult(
    ExecutableQuery? Query,
    IReadOnlyList<ValidationDiagnostic> Diagnostics)
{
    public bool IsValid => Query is not null && Diagnostics.Count == 0;
}
