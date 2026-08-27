namespace TurboBoard.Persistence;

public sealed class SavedQueryRecord
{
    public Guid Id { get; set; }
    public Guid DataSourceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DefinitionJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
