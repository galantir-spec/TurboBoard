using Microsoft.Data.SqlClient;

namespace TurboBoard.SqlServer;

public static class SqlServerConnectionString
{
    public static string Create(SqlServerConnectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var builder = settings.Mode switch
        {
            SqlServerConnectionMode.Structured => CreateStructured(settings),
            SqlServerConnectionMode.Advanced => new SqlConnectionStringBuilder(settings.ConnectionString),
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
