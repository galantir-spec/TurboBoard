using System.Reflection;

namespace TurboBoard.Core.Tests;

public sealed class ModuleBoundaryTests
{
    [Fact]
    public void Core_has_no_web_persistence_or_provider_dependencies()
    {
        var core = Assembly.Load("TurboBoard.Core");
        var referencedAssemblies = core
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
            name => name.StartsWith("Microsoft.Data.SqlClient", StringComparison.Ordinal));
        Assert.DoesNotContain(
            referencedAssemblies,
            name => name.Equals("TurboBoard.SqlServer", StringComparison.Ordinal));
    }
}
