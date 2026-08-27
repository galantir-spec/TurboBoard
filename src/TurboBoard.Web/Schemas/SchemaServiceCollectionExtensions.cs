namespace TurboBoard.Web.Schemas;

public static class SchemaServiceCollectionExtensions
{
    public static IServiceCollection AddTurboBoardSchemas(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddMemoryCache();
        services.AddSingleton<SchemaRefreshCoordinator>();
        services.AddScoped<ISchemaService, SchemaService>();
        return services;
    }
}
