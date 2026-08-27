namespace TurboBoard.Web.Hosting;

internal sealed record ApplicationStatePaths(
    string DatabasePath,
    string KeyRingDirectory)
{
    internal const string InitializationFailureMessage =
        "TurboBoard durable state could not be initialized. " +
        "Verify that the configured state directory is valid and writable.";

    private const string StateDirectoryConfigurationKey = "TurboBoard:StateDirectory";

    public static ApplicationStatePaths Prepare(
        IConfiguration configuration,
        string contentRootPath)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        var configuredPath = configuration[StateDirectoryConfigurationKey];
        var stateDirectory = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(contentRootPath, "App_Data")
            : Path.GetFullPath(configuredPath, contentRootPath);
        var keyRingDirectory = Path.Combine(stateDirectory, "keys");

        try
        {
            Directory.CreateDirectory(stateDirectory);
            Directory.CreateDirectory(keyRingDirectory);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            throw new InvalidOperationException(
                InitializationFailureMessage,
                exception);
        }

        return new ApplicationStatePaths(
            Path.Combine(stateDirectory, "turboboard.db"),
            keyRingDirectory);
    }
}
