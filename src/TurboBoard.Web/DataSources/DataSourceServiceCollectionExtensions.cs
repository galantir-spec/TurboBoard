namespace TurboBoard.Web.DataSources;

public static class DataSourceServiceCollectionExtensions
{
    public static IServiceCollection AddTurboBoardDataSources(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IDataSourceService, DataSourceService>();
        return services;
    }
}
