using System.Text.Json.Nodes;
using Xunit;

namespace InstructionTemplateSpecification.Compiler.Tests;

public class ObjectReferenceDataTests
{
    private static JsonObject Template(string text) => new()
    {
        ["version"] = "1.0.0",
        ["variables"] = new JsonObject
        {
            ["school"] = new JsonObject { ["name"] = "Riverbank Secondary", ["students"] = 940 },
            ["product"] = new JsonObject
            {
                ["details"] = new JsonObject { ["weight"] = "1.2kg", ["battery"] = "12h" },
            },
        },
        ["content"] = new JsonArray
        {
            new JsonObject { ["type"] = "text", ["text"] = text },
        },
    };

    [Fact]
    public async Task Pointer_substitution_and_field_table()
    {
        var result = await new ItsCompiler().CompileAsync(Template("Prepared for ${school}."));

        Assert.Contains("Prepared for the school reference data.", result.Prompt);
        Assert.DoesNotContain("[Object with", result.Prompt);
        Assert.Contains("### school", result.Prompt);
        Assert.Contains("| name | Riverbank Secondary |", result.Prompt);
        Assert.Contains("| students | 940 |", result.Prompt);
        Assert.Contains(ReferenceData.Instruction, result.Prompt);
    }

    [Fact]
    public async Task Nested_object_path_names_the_section()
    {
        var result = await new ItsCompiler().CompileAsync(Template("Specs: ${product.details}."));

        Assert.Contains("Specs: the product.details reference data.", result.Prompt);
        Assert.Contains("### product.details", result.Prompt);
        Assert.Contains("| weight | 1.2kg |", result.Prompt);
    }

    [Fact]
    public async Task Deduplicates_with_explicit_data_source()
    {
        var template = Template("About ${school}.");
        template["customInstructionTypes"] = new JsonObject
        {
            ["summary"] = new JsonObject
            {
                ["template"] = "<<Summarise: ([{<{description}>}]).>>",
            },
        };
        ((JsonArray)template["content"]!).Add(new JsonObject
        {
            ["type"] = "placeholder",
            ["instructionType"] = "summary",
            ["config"] = new JsonObject
            {
                ["description"] = "Summarise the school reference data",
                ["dataSource"] = "school",
            },
        });

        var result = await new ItsCompiler().CompileAsync(template);

        Assert.Equal(1, CountOccurrences(result.Prompt, "### school"));
    }

    [Fact]
    public async Task Scalars_and_arrays_unchanged()
    {
        var result = await new ItsCompiler().CompileAsync(
            Template("Name: ${school.name}, students: ${school.students}."));

        Assert.Contains("Name: Riverbank Secondary, students: 940.", result.Prompt);
        Assert.DoesNotContain("REFERENCE DATA", result.Prompt);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
