using Bunit;
using Microsoft.Extensions.DependencyInjection;
using TurboBoard.Core.Queries;
using TurboBoard.Core.Schemas;
using TurboBoard.Web.Components.Pages;
using TurboBoard.Web.DataSources;
using TurboBoard.Web.Queries;
using TurboBoard.Web.Schemas;
using QueriesPage = TurboBoard.Web.Components.Pages.Queries;

namespace TurboBoard.Web.Tests;

public sealed class QueriesComponentTests
{
    [Fact]
    public void Explicit_save_and_reset_commands_control_dirty_editor_state()
    {
        using var context = CreateContext(out var savedQueries);
        var component = context.Render<QueriesPage>();

        component.FindAll("select")[1].Change(TestDataSource.Id.ToString());
        component.FindAll("select")[2].Change("sales.Orders");
        component.Find("input[maxlength='200']").Input("Orders by id");
        component.Find("input[type='checkbox']").Change(true);

        Assert.Contains("You have unsaved changes", component.Markup, StringComparison.Ordinal);
        component.FindAll("button").Single(button => button.TextContent.Trim() == "Save As").Click();

        Assert.Equal(1, savedQueries.SaveCalls);
        Assert.DoesNotContain("You have unsaved changes", component.Markup, StringComparison.Ordinal);

        component.Find("input[maxlength='2000']").Input("Changed locally");
        component.FindAll("button").Single(button => button.TextContent.Trim() == "Reset").Click();

        Assert.DoesNotContain("Changed locally", component.Find("input[maxlength='2000']").GetAttribute("value"));
        Assert.Equal(1, savedQueries.SaveCalls);
    }

    [Fact]
    public void Duplicate_and_delete_commands_use_the_selected_saved_query()
    {
        using var context = CreateContext(out var savedQueries);
        savedQueries.SeedCurrent();
        var component = context.Render<QueriesPage>();

        component.FindAll("select")[1].Change(TestDataSource.Id.ToString());
        component.FindAll("select")[0].Change(savedQueries.CurrentId.ToString());
        component.FindAll("button").Single(button => button.TextContent.Trim() == "Duplicate").Click();
        component.FindAll("button").Single(button => button.TextContent.Trim() == "Delete").Click();

        Assert.Equal(1, savedQueries.DuplicateCalls);
        Assert.Equal(1, savedQueries.DeleteCalls);
    }

    [Fact]
    public void Unsupported_versions_show_a_diagnostic_and_allow_metadata_updates()
    {
        using var context = CreateContext(out var savedQueries);
        savedQueries.SeedUnsupported();
        var component = context.Render<QueriesPage>();

        component.FindAll("select")[1].Change(TestDataSource.Id.ToString());
        component.FindAll("select")[0].Change(savedQueries.CurrentId.ToString());
        component.Find("input[maxlength='200']").Input("Future renamed");
        component.FindAll("button").Single(button => button.TextContent.Trim() == "Save").Click();

        Assert.Contains("query.definition.version.unsupported", component.Markup, StringComparison.Ordinal);
        Assert.Equal(1, savedQueries.MetadataUpdateCalls);
    }

    [Fact]
    public void Filter_editor_offers_only_operators_compatible_with_the_column_type()
    {
        using var context = CreateContext(out _);
        var component = context.Render<QueriesPage>();
        component.FindAll("select")[1].Change(TestDataSource.Id.ToString());
        component.FindAll("select")[2].Change("sales.Orders");
        Assert.Contains("Every value is sent to the database as a parameter", component.Markup, StringComparison.Ordinal);

        component.FindAll("button").Single(button => button.TextContent.Trim() == "Add filter").Click();

        var operatorSelect = component.FindAll("select")[4];
        Assert.Contains("Between", operatorSelect.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Like", operatorSelect.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Filter_values_flow_into_preview_and_explicit_save_definitions()
    {
        using var context = CreateContext(out var savedQueries);
        var previews = context.Services.GetRequiredService<IQueryPreviewService>() as FakePreviewService;
        var component = context.Render<QueriesPage>();
        component.FindAll("select")[1].Change(TestDataSource.Id.ToString());
        component.FindAll("select")[2].Change("sales.Orders");
        component.Find("input[maxlength='200']").Input("Filtered orders");
        component.Find("input[type='checkbox']").Change(true);
        component.FindAll("button").Single(button => button.TextContent.Trim() == "Add filter").Click();
        component.Find(".filter-row input").Input("42");

        component.FindAll("button").Single(button => button.TextContent.Trim() == "Run preview").Click();
        component.FindAll("button").Single(button => button.TextContent.Trim() == "Save As").Click();

        var previewFilter = Assert.Single(previews!.LastDefinition!.AvailableFilters);
        var savedFilter = Assert.Single(savedQueries.LastDraft!.Definition.AvailableFilters);
        Assert.Equal(QueryFilterOperator.Equal, previewFilter.Operator);
        Assert.Equal("42", Assert.Single(previewFilter.Values));
        Assert.Equal(previewFilter.SourceId, savedFilter.SourceId);
        Assert.Equal(previewFilter.ColumnName, savedFilter.ColumnName);
        Assert.Equal(previewFilter.Operator, savedFilter.Operator);
        Assert.Equal(previewFilter.Values, savedFilter.Values);
    }

    [Fact]
    public void Filter_editor_adapts_value_controls_to_operator_arity()
    {
        using var context = CreateContext(out _);
        var component = context.Render<QueriesPage>();
        component.FindAll("select")[1].Change(TestDataSource.Id.ToString());
        component.FindAll("select")[2].Change("sales.Orders");
        component.FindAll("button").Single(button => button.TextContent.Trim() == "Add filter").Click();
        var operatorSelect = component.FindAll("select")[4];

        operatorSelect.Change(QueryFilterOperator.Between.ToString());
        Assert.Equal(2, component.FindAll("input:not([maxlength]):not([type='checkbox'])").Count);

        component.FindAll("select")[4].Change(QueryFilterOperator.In.ToString());
        Assert.Contains("comma separated", component.Markup, StringComparison.Ordinal);

        component.FindAll("select")[4].Change(QueryFilterOperator.IsNull.ToString());
        Assert.Empty(component.FindAll("input:not([maxlength]):not([type='checkbox'])"));
    }

    private static BunitContext CreateContext(out FakeSavedQueryService savedQueries)
    {
        var context = new BunitContext();
        savedQueries = new FakeSavedQueryService();
        context.Services.AddSingleton<IDataSourceService>(new FakeDataSourceService());
        context.Services.AddSingleton<ISchemaService>(new FakeSchemaService());
        context.Services.AddSingleton<IQueryPreviewService>(new FakePreviewService());
        context.Services.AddSingleton<ISavedQueryService>(savedQueries);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        return context;
    }

    private static class TestDataSource
    {
        public static readonly Guid Id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public static readonly Guid SourceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        public static readonly QualifiedDatabaseObjectName ObjectName = new("sales", "Orders");
        public static QueryDefinition Definition => new(
            QueryDefinition.CurrentVersion,
            new(TestDataSource.SourceId, ObjectName),
            [new(TestDataSource.SourceId, "Id", "Id")]);
    }

    private sealed class FakeDataSourceService : IDataSourceService
    {
        public Task<IReadOnlyList<DataSourceSummary>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DataSourceSummary>>([new(TestDataSource.Id, "Warehouse", "", default, "test", false, DateTimeOffset.UtcNow)]);
        public Task<DataSourceDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<DataSourceDetails?>(null);
        public Task<Guid> SaveAsync(Guid? id, DataSourceDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TurboBoard.Core.DataSources.DataSourceConnectionTestResult> TestAsync(Guid? id, DataSourceDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeSchemaService : ISchemaService
    {
        private static readonly DataSourceSchema Schema = new(
            TestDataSource.Id,
            DateTimeOffset.UtcNow,
            [new SchemaDatabaseObject(TestDataSource.ObjectName, DatabaseObjectKind.Table, [new SchemaColumn("Id", 1, NormalizedTypeCategory.Integer, "int", false, 4, 10, 0, SchemaColumnCapabilities.Select | SchemaColumnCapabilities.Filter)])],
            []);
        public Task<DataSourceSchema?> GetAsync(Guid dataSourceId, CancellationToken cancellationToken = default) => Task.FromResult<DataSourceSchema?>(Schema);
        public Task<SchemaState?> GetStateAsync(Guid dataSourceId, CancellationToken cancellationToken = default) => Task.FromResult<SchemaState?>(new(Schema, null, null, null));
        public Task<SchemaRefreshResult> RefreshAsync(Guid dataSourceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakePreviewService : IQueryPreviewService
    {
        public QueryDefinition? LastDefinition { get; private set; }
        public Task<QueryPreviewResponse> PreviewAsync(Guid dataSourceId, QueryDefinition definition, CancellationToken cancellationToken = default)
        {
            LastDefinition = definition;
            return Task.FromResult(new QueryPreviewResponse(QueryPreviewStatus.Succeeded, [], "SELECT", null));
        }
    }

    private sealed class FakeSavedQueryService : ISavedQueryService
    {
        private readonly List<SavedQueryDetails> items = [];
        public Guid CurrentId { get; private set; }
        public int SaveCalls { get; private set; }
        public int MetadataUpdateCalls { get; private set; }
        public int DuplicateCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public SavedQueryDraft? LastDraft { get; private set; }

        public void SeedCurrent() => Seed(TestDataSource.Definition, null);
        public void SeedUnsupported() => Seed(null, new("query.definition.version.unsupported", "Newer version."));
        private void Seed(QueryDefinition? definition, ValidationDiagnostic? diagnostic)
        {
            CurrentId = Guid.NewGuid();
            items.Add(new(CurrentId, TestDataSource.Id, "Orders", "", definition, diagnostic, DateTimeOffset.UtcNow));
        }

        public Task<IReadOnlyList<SavedQuerySummary>> ListAsync(Guid dataSourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SavedQuerySummary>>(items.Select(item => new SavedQuerySummary(item.Id, item.DataSourceId, item.Name, item.Description, item.UpdatedAtUtc)).ToArray());
        public Task<SavedQueryDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(items.SingleOrDefault(item => item.Id == id));
        public Task<Guid> SaveAsync(Guid? id, SavedQueryDraft draft, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            LastDraft = draft;
            CurrentId = id ?? Guid.NewGuid();
            items.RemoveAll(item => item.Id == CurrentId);
            items.Add(new(CurrentId, draft.DataSourceId, draft.Name, draft.Description, draft.Definition, null, DateTimeOffset.UtcNow));
            return Task.FromResult(CurrentId);
        }
        public Task UpdateMetadataAsync(Guid id, string name, string description, CancellationToken cancellationToken = default) { MetadataUpdateCalls++; return Task.CompletedTask; }
        public Task<Guid> DuplicateAsync(Guid id, string name, CancellationToken cancellationToken = default) { DuplicateCalls++; var copy = Guid.NewGuid(); var original = items.Single(item => item.Id == id); items.Add(original with { Id = copy, Name = name }); return Task.FromResult(copy); }
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) { DeleteCalls++; items.RemoveAll(item => item.Id == id); return Task.FromResult(true); }
    }
}
