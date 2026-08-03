using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace InstructionTemplateSpecification.Compiler.Tests;

/// <summary>
/// Cross-compiler type-rendering parity.
/// </summary>
/// <remarks>
/// The golden-prompt comparison checks one whole template byte-for-byte, which
/// is a strong check but a narrow one: that template contains no <c>false</c>,
/// no <c>null</c> and no negative numbers. A boolean rendering divergence
/// lived in the compilers for months and reached the published example outputs
/// without failing anything.
///
/// This fixture is broad instead. Every line is <c>name=${expression}</c>, so a
/// divergence names itself. The same fixture and the same expectations are
/// asserted by its-compiler (Python) and its-compiler-js, which is what makes
/// it a parity test rather than a snapshot.
///
/// If a value here changes, the three compilers have diverged. Fix the
/// divergence; do not edit the expectation to match one implementation.
/// </remarks>
public class TypeRenderingParityTests
{
    private static readonly string Fixture =
        Path.Combine(AppContext.BaseDirectory, "fixtures", "type-rendering.json");

    /// <summary>The canonical rendering every implementation must produce.</summary>
    public static readonly Dictionary<string, string> Expected = new()
    {
        // Booleans and null are lowercase words, never a language's own
        // spelling. This compiler used to render null as an empty string,
        // which lost the difference between null and "".
        ["bool-true"] = "true",
        ["bool-false"] = "false",
        ["null"] = "null",

        // Whole values carry no decimal part, however they were written.
        ["whole-float"] = "1",
        ["fraction"] = "0.5",
        ["repeating"] = "0.1",
        ["negative"] = "-42",
        ["negative-fraction"] = "-0.25",
        ["zero"] = "0",
        ["big"] = "1000000000000000",

        // Exponents: lowercase e, no plus, no leading zeros. This compiler
        // used to emit 1E-07 and Python emitted 1e-07.
        ["small"] = "1e-7",
        ["precise"] = "1.005",

        // Arrays join with ", " after each element is rendered by the rules.
        ["array-strings"] = "alpha, beta",
        ["array-mixed"] = "1, true, null, x, 2.5",

        ["unicode"] = "café — naïve ☂",
        ["quoted"] = "she said \"hi\"",

        ["len-array"] = "2",
        ["len-string"] = "14",

        // Float arithmetic is IEEE 754 everywhere, so the artefacts match too.
        ["sum-int"] = "10",
        ["avg-int"] = "2.5",
        ["min"] = "1",
        ["max"] = "4",
        ["sum-float"] = "0.30000000000000004",
        ["avg-thirds"] = "1",
        ["avg-money"] = "0.15000000000000002",

        ["concat-scalars"] = "alpha, beta",
        ["concat-mixed"] = "1, true, null, x, 2.5",
        ["concat-prop"] = "a, b",
        ["sum-prop"] = "4",
        ["concat-flags"] = "true, false",

        ["top2"] = "1, 2",
        ["index-neg"] = "beta",
        ["index-0"] = "alpha",

        ["cond-bool"] = "taken",
        ["cond-float-eq-int"] = "taken",
        ["cond-in-and-negative"] = "taken",
    };

    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var pair in Expected.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            data.Add(pair.Key, pair.Value);
        }
        return data;
    }

    private static async Task<Dictionary<string, string>> RenderAsync(string culture)
    {
        // Forced culture, because a comma decimal separator is exactly the
        // kind of locale-dependent quirk this test exists to rule out.
        var previous = CultureInfo.DefaultThreadCurrentCulture;
        CultureInfo.DefaultThreadCurrentCulture = new CultureInfo(culture);
        try
        {
            // The fixture extends nothing and has no placeholders, so this
            // needs no network and no schema files.
            var result = await new ItsCompiler().CompileFileAsync(Fixture);
            var values = new Dictionary<string, string>();
            foreach (var line in result.Prompt.Split('\n'))
            {
                var match = Regex.Match(line.TrimEnd('\r'), "^([a-z0-9-]+)=(.*)$");
                if (match.Success)
                {
                    values[match.Groups[1].Value] = match.Groups[2].Value;
                }
            }
            return values;
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = previous;
        }
    }

    [Fact]
    public async Task RendersEveryValueInTheFixture()
    {
        var rendered = await RenderAsync("en-AU");
        Assert.Equal(
            Expected.Keys.OrderBy(k => k, StringComparer.Ordinal),
            rendered.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ValueMatchesTheOtherCompilers(string key, string expected)
    {
        var rendered = await RenderAsync("en-AU");
        Assert.Equal(expected, rendered[key]);
    }

    [Fact]
    public async Task RenderingDoesNotDependOnTheCurrentCulture()
    {
        // de-DE writes decimals with a comma. If any number reached the output
        // through culture-sensitive formatting, this would differ.
        var invariantish = await RenderAsync("en-AU");
        var german = await RenderAsync("de-DE");
        Assert.Equal(invariantish, german);
    }
}
