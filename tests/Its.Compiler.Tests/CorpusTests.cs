using Xunit;

namespace Its.Compiler.Tests;

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

    public static TheoryData<string> BlockedTemplates()
    {
        var data = new TheoryData<string>();
        foreach (var subdirectory in new[] { "invalid", "security" })
        {
            foreach (var file in Directory.GetFiles(Path.Combine(CorpusRoot, subdirectory), "*.json").OrderBy(name => name))
            {
                data.Add(Path.Combine(subdirectory, Path.GetFileName(file)));
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(ValidTemplates))]
    public async Task Valid_corpus_templates_compile(string name)
    {
        var result = await SharedCompiler.CompileFileAsync(Path.Combine(CorpusRoot, name));
        Assert.Contains("TEMPLATE", result.Prompt);
    }

    [Theory]
    [MemberData(nameof(BlockedTemplates))]
    public async Task Invalid_and_security_corpus_templates_are_blocked(string name)
    {
        await Assert.ThrowsAnyAsync<ItsCompilationException>(
            () => SharedCompiler.CompileFileAsync(Path.Combine(CorpusRoot, name)));
    }
}
