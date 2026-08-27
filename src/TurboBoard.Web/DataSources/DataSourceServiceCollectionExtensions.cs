using TurboBoard.Core.DataSources;
using TurboBoard.Core.Schemas;

namespace TurboBoard.Web.DataSources;

public static class DataSourceServiceCollectionExtensions
{
    public static IServiceCollection AddTurboBoardDataSources(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(serviceProvider => new DataSourceProviderRegistry(
            serviceProvider.GetServices<IDataSourceConnectionTester>(),
            serviceProvider.GetServices<IDataSourceSchemaDiscoverer>()));
        services.AddScoped<DataSourceService>();
        services.AddScoped<IDataSourceService>(serviceProvider =>
            serviceProvider.GetRequiredService<DataSourceService>());
        services.AddScoped<IDataSourceConnectionRequestResolver>(serviceProvider =>
            serviceProvider.GetRequiredService<DataSourceService>());
        return services;
    }
}
