using TurboBoard.Core.DataSources;

namespace TurboBoard.SqlServer;

internal static class SqlServerConnectionRequest
{
    public static SqlServerConnectionSettings ToSettings(DataSourceConnectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Mode == DataSourceConnectionMode.Advanced)
        {
            return SqlServerConnectionSettings.CreateAdvanced(
                request.Secret ?? string.Empty,
                request.TrustServerCertificate);
        }

        _ = request.Properties.TryGetValue(DataSourceConnectionPropertyNames.Endpoint, out var server);
        _ = request.Properties.TryGetValue(DataSourceConnectionPropertyNames.Catalog, out var database);
        _ = request.Properties.TryGetValue(DataSourceConnectionPropertyNames.UserName, out var userName);
        _ = request.Properties.TryGetValue(DataSourceConnectionPropertyNames.IntegratedAuthentication, out var integratedSecurity);
        return SqlServerConnectionSettings.CreateStructured(
            server ?? string.Empty,
            database ?? string.Empty,
            bool.TryParse(integratedSecurity, out var integrated) && integrated,
            userName,
            request.Secret,
            request.TrustServerCertificate);
    }
}
