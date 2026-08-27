using TurboBoard.Core.DataSources;
using TurboBoard.Core.Schemas;
using TurboBoard.Core.Queries;

namespace TurboBoard.Web.DataSources;

public static class DataSourceServiceCollectionExtensions
{
    public static IServiceCollection AddTurboBoardDataSources(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(serviceProvider => new DataSourceProviderRegistry(
            serviceProvider.GetServices<IDataSourceConnectionTester>(),
            serviceProvider.GetServices<IDataSourceSchemaDiscoverer>(),
            serviceProvider.GetServices<IQueryCompiler>(),
            serviceProvider.GetServices<IQueryExecutor>()));
        services.AddScoped<DataSourceService>();
        services.AddScoped<IDataSourceService>(serviceProvider =>
            serviceProvider.GetRequiredService<DataSourceService>());
        services.AddScoped<IDataSourceConnectionRequestResolver>(serviceProvider =>
            serviceProvider.GetRequiredService<DataSourceService>());
        return services;
    }
}
