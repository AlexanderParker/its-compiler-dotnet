using Xunit;

namespace InstructionTemplateSpecification.Compiler.Tests;

/// <summary>
/// Cross-compiler parity: the compiled prompt must match the Python
/// reference compiler byte for byte (modulo line endings) for the shared
/// fixture, so all three compilers stay interchangeable.
/// </summary>
public class GoldenPromptTests
{
    [Fact]
    public async Task Compiled_prompt_matches_the_python_reference_compiler()
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        var expected = Normalise(await File.ReadAllTextAsync(Path.Combine(fixtures, "golden", "json-types-template.prompt.txt")));

        var compiler = new ItsCompiler(new CompilerOptions { AllowLocalFileSchemas = true });
        var result = await compiler.CompileFileAsync(Path.Combine(fixtures, "json-types-template.json"));

        Assert.Equal(expected, Normalise(result.Prompt));
    }

    private static string Normalise(string text) => text.Replace("\r\n", "\n").TrimEnd('\n');
}
