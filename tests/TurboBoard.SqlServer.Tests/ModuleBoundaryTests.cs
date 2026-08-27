using System.Reflection;

namespace TurboBoard.SqlServer.Tests;

public sealed class ModuleBoundaryTests
{
    [Fact]
    public void SqlServer_provider_has_no_web_or_application_persistence_dependencies()
    {
        var provider = Assembly.Load("TurboBoard.SqlServer");
        var referencedAssemblies = provider
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(
            referencedAssemblies,
            name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.DoesNotContain(
            referencedAssemblies,
            name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(
            referencedAssemblies,
            name => name.Equals("TurboBoard.Persistence", StringComparison.Ordinal));
        Assert.DoesNotContain(
            referencedAssemblies,
            name => name.Equals("TurboBoard.Web", StringComparison.Ordinal));
    }
}
