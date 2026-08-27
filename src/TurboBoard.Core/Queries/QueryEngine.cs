using System.Text.RegularExpressions;
using TurboBoard.Core.Schemas;

namespace TurboBoard.Core.Queries;

public static partial class QueryEngine
{
    public static QueryPreparationResult Prepare(DataSourceSchema schema, QueryDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(definition);
        var diagnostics = new List<ValidationDiagnostic>();
        if (definition.Version != QueryDefinition.CurrentVersion)
        {
            diagnostics.Add(new("query.version.unsupported", "This Query Definition version is not supported."));
        }

        if (definition.Source.Id == Guid.Empty)
        {
            diagnostics.Add(new("query.source.id-required", "The selected source needs a stable identity."));
        }

        var source = schema.Objects.SingleOrDefault(item => item.QualifiedName == definition.Source.Object);
        if (source is null)
        {
            diagnostics.Add(new("query.source.unknown", $"Source '{definition.Source.Object.DisplayName}' is not in the current Schema."));
        }

        if (definition.Selections.Count == 0)
        {
            diagnostics.Add(new("query.selection.required", "Select at least one column."));
        }

        var executableSelections = new List<ExecutableSelection>();
        var outputNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selection in definition.Selections)
        {
            if (selection.SourceId != definition.Source.Id)
            {
                diagnostics.Add(new("query.selection.source-unknown", $"Column '{selection.ColumnName}' refers to an unknown source."));
                continue;
            }

            var column = source?.Columns.SingleOrDefault(item =>
                string.Equals(item.Name, selection.ColumnName, StringComparison.OrdinalIgnoreCase));
            if (column is null || !column.Capabilities.HasFlag(SchemaColumnCapabilities.Select))
            {
                diagnostics.Add(new("query.selection.column-unknown", $"Column '{selection.ColumnName}' is not selectable from the current Schema."));
                continue;
            }

            if (!OutputNamePattern().IsMatch(selection.OutputName))
            {
                diagnostics.Add(new("query.selection.alias-invalid", $"Output name '{selection.OutputName}' is not valid."));
                continue;
            }

            if (!outputNames.Add(selection.OutputName))
            {
                diagnostics.Add(new("query.selection.alias-duplicate", $"Output name '{selection.OutputName}' is used more than once."));
                continue;
            }

            executableSelections.Add(new(column, selection.OutputName));
        }

        return diagnostics.Count == 0 && source is not null
            ? new(new ExecutableQuery(definition.Source.Id, source, executableSelections), diagnostics)
            : new(null, diagnostics);
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex OutputNamePattern();
}
