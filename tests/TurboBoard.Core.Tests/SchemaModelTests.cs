using TurboBoard.Core.Schemas;

namespace TurboBoard.Core.Tests;

public sealed class SchemaModelTests
{
    [Fact]
    public void Relationship_lookup_returns_every_valid_path_without_guessing()
    {
        var orders = new QualifiedDatabaseObjectName("sales", "Orders");
        var customers = new QualifiedDatabaseObjectName("crm", "Customers");
        var schema = new DataSourceSchema(Guid.NewGuid(), DateTimeOffset.UtcNow, [],
        [
            new("FK_Orders_BillTo", orders, ["BillToId"], customers, ["Id"]),
            new("FK_Orders_ShipTo", orders, ["ShipToId"], customers, ["Id"]),
        ]);

        var paths = schema.FindRelationships(orders, customers);

        Assert.Equal(2, paths.Count);
        Assert.Contains(paths, path => path.Name == "FK_Orders_BillTo");
        Assert.Contains(paths, path => path.Name == "FK_Orders_ShipTo");
    }

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
