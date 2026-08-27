using TurboBoard.Core.Schemas;

namespace TurboBoard.Core.Tests;

public sealed class SchemaModelTests
{
    [Fact]
    public void Qualified_objects_with_the_same_name_in_different_schemas_remain_distinct()
    {
        var sales = new QualifiedDatabaseObjectName("sales", "Orders");
        var archive = new QualifiedDatabaseObjectName("archive", "Orders");

        Assert.NotEqual(sales, archive);
        Assert.Equal("sales.Orders", sales.DisplayName);
        Assert.Equal("archive.Orders", archive.DisplayName);
    }

    [Fact]
    public void Unknown_provider_types_remain_visible_without_query_capabilities()
    {
        var column = new SchemaColumn(
            "Location",
            1,
            NormalizedTypeCategory.Unknown,
            "geography",
            IsNullable: false,
            MaximumLength: null,
            Precision: null,
            Scale: null,
            SchemaColumnCapabilities.None);

        Assert.Equal("geography", column.ProviderType);
        Assert.Equal(SchemaColumnCapabilities.None, column.Capabilities);
    }
}
