using TurboBoard.Core.Schemas;

namespace TurboBoard.Core.Queries;

public sealed record QuerySource(Guid Id, QualifiedDatabaseObjectName Object);

public sealed record QuerySelection(Guid SourceId, string ColumnName, string OutputName);

public sealed record QueryDefinition(int Version, QuerySource Source, IReadOnlyList<QuerySelection> Selections)
{
    public const int CurrentVersion = 1;
}

public sealed record ValidationDiagnostic(string Code, string Message);

public sealed record ExecutableSelection(SchemaColumn Column, string OutputName);

public sealed class ExecutableQuery
{
    internal ExecutableQuery(
        Guid sourceId,
        SchemaDatabaseObject source,
        IReadOnlyList<ExecutableSelection> selections)
    {
        SourceId = sourceId;
        Source = source;
        Selections = selections;
    }

    public Guid SourceId { get; }
    public SchemaDatabaseObject Source { get; }
    public IReadOnlyList<ExecutableSelection> Selections { get; }
}

public sealed record QueryPreparationResult(
    ExecutableQuery? Query,
    IReadOnlyList<ValidationDiagnostic> Diagnostics)
{
    public bool IsValid => Query is not null && Diagnostics.Count == 0;
}
