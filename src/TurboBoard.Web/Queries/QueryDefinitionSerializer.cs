using System.Text.Json;
using TurboBoard.Core.Queries;

namespace TurboBoard.Web.Queries;

public sealed record QueryDefinitionReadResult(
    QueryDefinition? Definition,
    ValidationDiagnostic? Diagnostic);

public interface IQueryDefinitionSerializer
{
    string Serialize(QueryDefinition definition);
    QueryDefinitionReadResult Deserialize(string json);
}

internal sealed class QueryDefinitionSerializer : IQueryDefinitionSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Serialize(QueryDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Version != QueryDefinition.CurrentVersion)
            throw new SavedQueryValidationException(["Only the current Query Definition version can be saved."]);
        return JsonSerializer.Serialize(definition, JsonOptions);
    }

    public QueryDefinitionReadResult Deserialize(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("version", out var versionElement)
                || !versionElement.TryGetInt32(out var version))
                return Invalid("The Saved Query has no readable Query Definition version.");

            return version switch
            {
                QueryDefinition.CurrentVersion => ReadCurrent(json),
                2 => ReadVersionTwo(json),
                1 => ReadVersionOne(json),
                > QueryDefinition.CurrentVersion => new(
                    null,
                    new(
                        "query.definition.version.unsupported",
                        "This Saved Query uses a newer version. Its name and description remain editable, but its Query Definition cannot be edited or run safely in this TurboBoard version.")),
                _ => Invalid("The Saved Query uses an unsupported older Query Definition version."),
            };
        }
        catch (JsonException)
        {
            return Invalid("The Saved Query contains an unreadable Query Definition.");
        }
    }

    private static QueryDefinitionReadResult ReadCurrent(string json)
    {
        var definition = JsonSerializer.Deserialize<QueryDefinition>(json, JsonOptions);
        return definition is null
            ? Invalid("The Saved Query contains an empty Query Definition.")
            : new(definition, null);
    }

    private static QueryDefinitionReadResult ReadVersionOne(string json)
    {
        var legacy = JsonSerializer.Deserialize<QueryDefinition>(json, JsonOptions);
        if (legacy is null) return Invalid("The Saved Query contains an empty Query Definition.");
        QueryFilterExpression? expression = legacy.AvailableFilters.Count == 0
            ? null
            : new QueryFilterGroup(
                Guid.NewGuid(),
                true,
                QueryFilterGroupOperator.And,
                legacy.AvailableFilters.Select(filter => (QueryFilterExpression)new QueryFilterCondition(Guid.NewGuid(), true, filter)).ToArray());
        return new(new(QueryDefinition.CurrentVersion, legacy.Source, legacy.Selections, FilterExpression: expression), null);
    }

    private static QueryDefinitionReadResult ReadVersionTwo(string json)
    {
        var legacy = JsonSerializer.Deserialize<QueryDefinition>(json, JsonOptions);
        return legacy is null
            ? Invalid("The Saved Query contains an empty Query Definition.")
            : new(new(
                QueryDefinition.CurrentVersion,
                legacy.Source,
                legacy.Selections,
                legacy.Filters,
                legacy.FilterExpression,
                []), null);
    }

    private static QueryDefinitionReadResult Invalid(string message) =>
        new(null, new("query.definition.invalid", message));
}
