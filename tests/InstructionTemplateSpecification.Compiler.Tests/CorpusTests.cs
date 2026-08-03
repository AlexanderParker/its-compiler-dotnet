using Xunit;

namespace InstructionTemplateSpecification.Compiler.Tests;

/// <summary>
/// Runs the shared its-example-templates corpus, mirroring the Python
/// compiler's harness: every valid template compiles, every invalid and
/// security template is blocked. Valid templates extend the published
/// standard types schema, so these tests fetch it over the network once.
/// </summary>
public class CorpusTests
{
    private static readonly string CorpusRoot = Path.Combine(AppContext.BaseDirectory, "fixtures", "corpus");

    private static readonly ItsCompiler SharedCompiler = new();

    public static TheoryData<string> ValidTemplates()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(CorpusRoot, "*.json").OrderBy(name => name))
        {
            data.Add(Path.GetFileName(file));
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(ValidTemplates))]
    public async Task Valid_corpus_templates_compile(string name)
    {
        var result = await SharedCompiler.CompileFileAsync(Path.Combine(CorpusRoot, name));

        Assert.Contains("INTRODUCTION", result.Prompt);
        Assert.Contains("INSTRUCTIONS", result.Prompt);
        Assert.Contains("TEMPLATE", result.Prompt);
        // The template body must survive compilation with every reference resolved
        var body = result.Prompt[(result.Prompt.IndexOf("TEMPLATE", StringComparison.Ordinal) + "TEMPLATE".Length)..];
        Assert.True(body.Trim().Length > 20, $"Template body of {name} is empty");
        Assert.DoesNotContain("${", body);
    }

    // Each blocked template must be rejected for its own reason, not just
    // rejected: a network failure or unrelated guard must not satisfy these
    [Theory]
    [InlineData("invalid/01-invalid-json.json", "Invalid JSON in template file")]
    [InlineData("invalid/02-missing-required-fields.json", "version")]
    [InlineData("invalid/03-undefined-variables.json", "Undefined variable reference")]
    [InlineData("invalid/04-unknown-instruction-type.json", "Unknown instruction type")]
    [InlineData("invalid/05-invalid-conditional.json", "Undefined variable reference")]
    [InlineData("invalid/06-missing-placeholder-config.json", "missing its required description")]
    [InlineData("invalid/07-empty-content.json", "content must not be empty")]
    [InlineData("security/malicious_expressions.json", "condition")]
    [InlineData("security/malicious_injection.json", "Dangerous content pattern")]
    [InlineData("security/malicious_schema.json", "Too many extensions")]
    [InlineData("security/malicious_variables.json", "Dangerous")]
    public async Task Invalid_and_security_corpus_templates_are_blocked(string name, string expectedMessage)
    {
        var error = await Assert.ThrowsAnyAsync<ItsCompilationException>(
            () => SharedCompiler.CompileFileAsync(Path.Combine(CorpusRoot, name.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Contains(expectedMessage, error.Message);
    }
}
