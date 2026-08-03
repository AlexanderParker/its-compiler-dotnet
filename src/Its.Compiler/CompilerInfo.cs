using System.Reflection;

namespace Its.Compiler;

/// <summary>
/// Version information for this compiler.
/// </summary>
/// <remarks>
/// Two versions are reported and they answer different questions. The package
/// version moves with fixes and features. The supported specification version
/// moves only when the Instruction Template Specification does, and is what
/// tells a caller whether their templates will compile.
///
/// This mirrors <c>__version__</c> and <c>__supported_schema_version__</c> in
/// its-compiler (Python), and <c>VERSION</c> and
/// <c>SUPPORTED_SCHEMA_VERSION</c> in its-compiler-js, so all three
/// implementations answer the question the same way.
/// </remarks>
public static class CompilerInfo
{
    /// <summary>
    /// The ITS specification version this compiler implements.
    /// </summary>
    public const string SupportedSchemaVersion = "1.0";

    /// <summary>
    /// The package version, read from assembly metadata rather than written
    /// here, so it cannot drift from the project file.
    /// </summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var assembly = typeof(CompilerInfo).Assembly;

        // InformationalVersion carries the value from <Version> in the project
        // file. Source Link appends a build identifier after a '+', which is
        // not part of the version a caller cares about.
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
