using TurboBoard.Core.DataSources;

namespace TurboBoard.Web.DataSources;

public static class DataSourceServiceCollectionExtensions
{
    public static IServiceCollection AddTurboBoardDataSources(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(serviceProvider => new DataSourceProviderRegistry(
            serviceProvider.GetServices<IDataSourceConnectionTester>()));
        services.AddScoped<IDataSourceService, DataSourceService>();
        return services;
    }
}
