using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using TurboBoard.Persistence;

namespace TurboBoard.Web.Hosting;

internal static class DurableApplicationState
{
    private const string StartupProbePurpose = "TurboBoard.StartupReadiness";

    public static async Task InitializeAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        try
        {
            var dataProtectionProvider = services.GetRequiredService<IDataProtectionProvider>();
            _ = dataProtectionProvider
                .CreateProtector(StartupProbePurpose)
                .Protect("readiness-probe");

            await services.InitializeTurboBoardPersistenceAsync(cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or CryptographicException
                or DbException)
        {
            throw new InvalidOperationException(
                ApplicationStatePaths.InitializationFailureMessage,
                exception);
        }
    }
}
