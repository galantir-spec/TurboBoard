using Microsoft.Extensions.DependencyInjection;
using TurboBoard.Core.DataSources;

namespace TurboBoard.SqlServer;

public static class SqlServerServiceCollectionExtensions
{
    public static IServiceCollection AddTurboBoardSqlServer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IDataSourceConnectionTester, SqlServerConnectionTester>();
        return services;
    }
}
