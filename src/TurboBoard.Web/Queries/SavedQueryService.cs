using Microsoft.EntityFrameworkCore;
using TurboBoard.Core.Queries;
using TurboBoard.Persistence;

namespace TurboBoard.Web.Queries;

internal sealed class SavedQueryService(
    IDbContextFactory<TurboBoardDbContext> contextFactory,
    IQueryDefinitionSerializer definitionSerializer)
    : ISavedQueryService
{
    public async Task<IReadOnlyList<SavedQuerySummary>> ListAsync(
        Guid? dataSourceId = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.SavedQueries.AsNoTracking();
        if (dataSourceId is Guid id) query = query.Where(item => item.DataSourceId == id);
        return await query
            .OrderBy(item => item.Name)
            .Select(item => new SavedQuerySummary(
                item.Id,
                item.DataSourceId,
                item.Name,
                item.Description,
                item.UpdatedAtUtc))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<SavedQueryDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.SavedQueries.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (record is null) return null;

        var decoded = definitionSerializer.Deserialize(record.DefinitionJson);
        return new(
            record.Id,
            record.DataSourceId,
            record.Name,
            record.Description,
            decoded.Definition,
            decoded.Diagnostic,
            record.UpdatedAtUtc);
    }

    public async Task<Guid> SaveAsync(
        Guid? id,
        SavedQueryDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        Validate(draft.Name, draft.Description, draft.Definition);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = id is null
            ? null
            : await context.SavedQueries.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("The Saved Query no longer exists.");
        if (existing is not null && existing.DataSourceId != draft.DataSourceId)
            throw new SavedQueryValidationException(["A Saved Query cannot be moved to another Data Source."]);

        var now = DateTimeOffset.UtcNow;
        var record = existing ?? new SavedQueryRecord { Id = Guid.NewGuid(), CreatedAtUtc = now };
        record.DataSourceId = draft.DataSourceId;
        record.Name = draft.Name.Trim();
        record.Description = draft.Description.Trim();
        record.DefinitionJson = definitionSerializer.Serialize(draft.Definition);
        record.UpdatedAtUtc = now;
        if (existing is null) context.SavedQueries.Add(record);
        await context.SaveChangesAsync(cancellationToken);
        return record.Id;
    }

    public async Task<Guid> DuplicateAsync(
        Guid id,
        string name,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var original = await context.SavedQueries.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("The Saved Query no longer exists.");
        ValidateMetadata(name, original.Description);
        var now = DateTimeOffset.UtcNow;
        var duplicate = new SavedQueryRecord
        {
            Id = Guid.NewGuid(),
            DataSourceId = original.DataSourceId,
            Name = name.Trim(),
            Description = original.Description,
            DefinitionJson = original.DefinitionJson,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        context.SavedQueries.Add(duplicate);
        await context.SaveChangesAsync(cancellationToken);
        return duplicate.Id;
    }

    public async Task UpdateMetadataAsync(
        Guid id,
        string name,
        string description,
        CancellationToken cancellationToken = default)
    {
        ValidateMetadata(name, description);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.SavedQueries.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("The Saved Query no longer exists.");
        record.Name = name.Trim();
        record.Description = description.Trim();
        record.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.SavedQueries.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (record is null) return false;
        context.SavedQueries.Remove(record);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static void Validate(string name, string description, QueryDefinition definition)
    {
        ValidateMetadata(name, description);
        ArgumentNullException.ThrowIfNull(definition);
    }

    private static void ValidateMetadata(string name, string description)
    {
        var diagnostics = new List<string>();
        if (string.IsNullOrWhiteSpace(name)) diagnostics.Add("Enter a Saved Query name.");
        else if (name.Trim().Length > 200) diagnostics.Add("The Saved Query name must be 200 characters or fewer.");
        if ((description ?? string.Empty).Trim().Length > 2000) diagnostics.Add("The description must be 2,000 characters or fewer.");
        if (diagnostics.Count > 0) throw new SavedQueryValidationException(diagnostics);
    }
}
