using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Its.Compiler.Tests;

/// <summary>
/// The two versions this compiler reports must stay distinct and must stay
/// true. A hardcoded version that drifts from the project file is the defect
/// that shipped in its-compiler-cli 1.1.0 and in the Python core before it.
/// </summary>
public class CompilerInfoTests
{
    [Fact]
    public void ReportsTheSpecificationVersionItImplements()
    {
        Assert.Equal("1.0", CompilerInfo.SupportedSchemaVersion);
    }

    [Fact]
    public void ReportsAPackageVersionReadFromAssemblyMetadata()
    {
        Assert.False(string.IsNullOrWhiteSpace(CompilerInfo.Version));
        Assert.NotEqual("0.0.0", CompilerInfo.Version);
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+"), CompilerInfo.Version);
    }

    [Fact]
    public void StripsAnyBuildIdentifierFromTheInformationalVersion()
    {
        // Source Link appends "+<commit>" to InformationalVersion, which is
        // not part of the version a caller asked for.
        Assert.DoesNotContain("+", CompilerInfo.Version);
    }

    [Fact]
    public void MatchesTheVersionDeclaredOnTheAssembly()
    {
        var informational = typeof(CompilerInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            Assert.Equal(informational.Split('+')[0], CompilerInfo.Version);
        }
    }

    [Fact]
    public void KeepsThePackageVersionAndSpecificationVersionDistinct()
    {
        // They answer different questions: the package version moves with
        // fixes, the specification version only when the spec changes.
        Assert.NotEqual(CompilerInfo.SupportedSchemaVersion, CompilerInfo.Version);
    }
}
