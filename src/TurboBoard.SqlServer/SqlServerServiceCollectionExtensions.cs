using Microsoft.Extensions.DependencyInjection;
using TurboBoard.Core.DataSources;
using TurboBoard.Core.Schemas;

namespace TurboBoard.SqlServer;

public static class SqlServerServiceCollectionExtensions
{
    public static IServiceCollection AddTurboBoardSqlServer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IDataSourceConnectionTester, SqlServerConnectionTester>();
        services.AddSingleton<ISqlServerCatalogReader, SqlServerCatalogReader>();
        services.AddSingleton<IDataSourceSchemaDiscoverer, SqlServerSchemaDiscoverer>();
        return services;
    }
}
