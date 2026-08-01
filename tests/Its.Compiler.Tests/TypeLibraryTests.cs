using System.Text.Json.Nodes;
using Xunit;

namespace Its.Compiler.Tests;

public class TypeLibraryTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static ItsCompiler LocalCompiler() =>
        new(new CompilerOptions { AllowLocalFileSchemas = true });

    private const string JsonClause =
        "Output raw, valid JSON only - no markdown code fences, no surrounding commentary, and no explanation.";

    private const string HtmlClause =
        "Output raw, valid HTML only - no markdown code fences, no surrounding commentary, and no explanation.";

    private const string MarkdownClause =
        "Output raw, valid Markdown only - no surrounding commentary and no explanation, "
        + "and do not wrap the output in code fences.";

    private const string MarkdownCodeClause =
        "Output raw code only - no code fences, no surrounding commentary and no explanation.";

    [Fact]
    public async Task Keeps_authored_json_structure_verbatim_with_fills()
    {
        var result = await LocalCompiler().CompileFileAsync(FixturePath("json-types-template.json"));

        Assert.Contains("{\n  \"data\": [\n", result.Prompt);
        Assert.Contains("\"page\": 1,", result.Prompt);
        Assert.Contains("\"code\": \"not_found\",", result.Prompt);
        Assert.Contains(JsonClause, result.Prompt);
        Assert.Contains("([{<three orders objects with id and status fields>}])", result.Prompt);
        Assert.Contains("without the enclosing square brackets", result.Prompt);
        Assert.Contains("of kind integer", result.Prompt);
    }

    [Fact]
    public async Task Evaluates_conditionals_from_variables()
    {
        var path = FixturePath("json-types-template.json");

        var withError = await LocalCompiler().CompileFileAsync(path);
        Assert.Contains("not_found", withError.Prompt);

        var overrides = new JsonObject { ["includeErrorExample"] = false };
        var withoutError = await LocalCompiler().CompileFileAsync(path, overrides);
        Assert.DoesNotContain("not_found", withoutError.Prompt);
    }

    [Fact]
    public async Task Renders_defaults_when_config_omitted()
    {
        var result = await LocalCompiler().CompileFileAsync(FixturePath("json-types-template.json"));

        Assert.Contains("of type any", result.Prompt);
        Assert.DoesNotContain("{valueType}", result.Prompt);
        Assert.DoesNotContain("{numberType}", result.Prompt);
    }

    [Fact]
    public async Task Renders_html_and_yaml_libraries()
    {
        var html = await LocalCompiler().CompileFileAsync(FixturePath("html-types-template.json"));
        Assert.Contains("<section class=\"product-card\">", html.Prompt);
        Assert.Contains("Inline markup such as strong, em and a is allowed: true.", html.Prompt);
        Assert.Contains("Include class attributes on elements: true.", html.Prompt);

        var yaml = await LocalCompiler().CompileFileAsync(FixturePath("yaml-types-template.json"));
        Assert.Contains("build:\n  script:\n", yaml.Prompt);
        Assert.Contains("beginning with 4 spaces followed by a hyphen", yaml.Prompt);
        Assert.Contains("indented by 2 spaces", yaml.Prompt);
    }

    [Fact]
    public async Task Generates_complete_element_from_html_list()
    {
        var template = new JsonObject
        {
            ["$schema"] =
                "https://alexanderparker.github.io/instruction-template-specification/schema/v1.0/its-base-schema-v1.json",
            ["version"] = "1.0.0",
            ["extends"] = new JsonArray("./its-html-types-v1.json"),
            ["content"] = new JsonArray(
                new JsonObject { ["type"] = "text", ["text"] = "<nav>\n" },
                new JsonObject
                {
                    ["type"] = "placeholder",
                    ["instructionType"] = "html_list",
                    ["config"] = new JsonObject
                    {
                        ["description"] = "links to the main documentation sections",
                        ["listType"] = "unordered",
                        ["itemCount"] = 4,
                    },
                },
                new JsonObject { ["type"] = "text", ["text"] = "\n</nav>" }),
        };

        var baseUrl = new Uri(Path.Combine(AppContext.BaseDirectory, "fixtures") + Path.DirectorySeparatorChar).AbsoluteUri;
        var result = await LocalCompiler().CompileAsync(template, baseUrl: baseUrl);

        Assert.Contains("Produce a complete unordered list element including its items", result.Prompt);
        Assert.Contains(HtmlClause, result.Prompt);
        Assert.Contains("([{<links to the main documentation sections>}])", result.Prompt);
    }

    [Fact]
    public async Task Keeps_authored_markdown_scaffolding_verbatim_with_fills()
    {
        var result = await LocalCompiler().CompileFileAsync(FixturePath("markdown-types-template.json"));

        Assert.Contains("# example-storefront release notes", result.Prompt);
        Assert.Contains("## Features\n", result.Prompt);
        Assert.Contains("| Package | Version |\n| --- | --- |\n", result.Prompt);
        Assert.Contains("## Installation\n\n```bash\n", result.Prompt);
        Assert.Contains("\n```", result.Prompt);
        Assert.Contains(MarkdownClause, result.Prompt);
        Assert.Contains(MarkdownCodeClause, result.Prompt);
        Assert.Contains("([{<three headline features of example-storefront>}])", result.Prompt);
        Assert.Contains("without producing a header or separator row", result.Prompt);
    }

    [Fact]
    public async Task Evaluates_markdown_conditionals_from_variables()
    {
        var path = FixturePath("markdown-types-template.json");

        var withInstall = await LocalCompiler().CompileFileAsync(path);
        Assert.Contains("## Installation", withInstall.Prompt);

        var overrides = new JsonObject { ["includeInstall"] = false };
        var withoutInstall = await LocalCompiler().CompileFileAsync(path, overrides);
        Assert.DoesNotContain("## Installation", withoutInstall.Prompt);
        Assert.DoesNotContain("```bash", withoutInstall.Prompt);
    }

    [Fact]
    public async Task Renders_markdown_defaults_when_config_omitted()
    {
        var result = await LocalCompiler().CompileFileAsync(FixturePath("markdown-types-template.json"));

        // markdown_list_items omits listType; the default is bullet
        Assert.Contains("using bullet markers", result.Prompt);
        Assert.DoesNotContain("{listType}", result.Prompt);
    }

    [Fact]
    public async Task Rejects_local_file_schemas_by_default()
    {
        var compiler = new ItsCompiler();
        await Assert.ThrowsAsync<ItsSecurityException>(
            () => compiler.CompileFileAsync(FixturePath("json-types-template.json")));
    }
}
