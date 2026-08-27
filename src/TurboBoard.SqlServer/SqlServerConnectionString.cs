using Microsoft.Data.SqlClient;
using TurboBoard.Core.DataSources;

namespace TurboBoard.SqlServer;

public static class SqlServerConnectionString
{
    public static string Create(SqlServerConnectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var builder = settings.Mode switch
        {
            DataSourceConnectionMode.Structured => CreateStructured(settings),
            DataSourceConnectionMode.Advanced => new SqlConnectionStringBuilder(settings.ConnectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.Mode, "Unsupported connection mode."),
        };

        builder.ApplicationName = "TurboBoard";
        builder.Encrypt = SqlConnectionEncryptOption.Mandatory;
        builder.TrustServerCertificate = settings.TrustServerCertificate;
        return builder.ConnectionString;
    }

    private static SqlConnectionStringBuilder CreateStructured(SqlServerConnectionSettings settings)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = settings.Server,
            InitialCatalog = settings.Database,
            IntegratedSecurity = settings.UseIntegratedSecurity,
        };

        if (!settings.UseIntegratedSecurity)
        {
            builder.UserID = settings.UserName;
            builder.Password = settings.Password;
        }

        return builder;
    }
}
