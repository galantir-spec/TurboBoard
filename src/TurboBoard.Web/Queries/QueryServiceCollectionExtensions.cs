namespace TurboBoard.Web.Queries;

public static class QueryServiceCollectionExtensions
{
    public static IServiceCollection AddTurboBoardQueries(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IQueryPreviewService, QueryPreviewService>();
        return services;
    }
}
