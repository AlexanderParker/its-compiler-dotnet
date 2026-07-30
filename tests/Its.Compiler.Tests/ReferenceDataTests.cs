using System.Text.Json.Nodes;
using Xunit;

namespace Its.Compiler.Tests;

public class ReferenceDataTests
{
    private static JsonObject ForecastTemplate() => new()
    {
        ["version"] = "1.0.0",
        ["customInstructionTypes"] = new JsonObject
        {
            ["summary"] = new JsonObject
            {
                ["template"] = "<<Summarise using this prompt: ([{<{description}>}]).>>",
            },
        },
        ["variables"] = new JsonObject
        {
            ["location"] = "Adelaide",
            ["forecast"] = new JsonArray
            {
                new JsonObject { ["day"] = "Monday", ["high"] = 29, ["wet"] = false },
                new JsonObject { ["day"] = "Tuesday", ["high"] = 32, ["wet"] = false },
                new JsonObject { ["day"] = "Sunday", ["high"] = 27, ["wet"] = true },
            },
        },
        ["content"] = new JsonArray
        {
            new JsonObject { ["type"] = "text", ["text"] = "# Briefing for ${location}\n\n" },
            new JsonObject
            {
                ["type"] = "placeholder",
                ["instructionType"] = "summary",
                ["config"] = new JsonObject
                {
                    ["description"] = "Summarise the trends in the forecast reference data",
                    ["dataSource"] = "forecast",
                },
            },
        },
    };

    [Fact]
    public async Task Renders_referenced_variables_above_the_template()
    {
        var result = await new ItsCompiler().CompileAsync(ForecastTemplate());

        Assert.Contains("REFERENCE DATA", result.Prompt);
        Assert.Contains("### forecast", result.Prompt);
        Assert.Contains("| day | high | wet |", result.Prompt);
        Assert.Contains("| Monday | 29 | false |", result.Prompt);
        Assert.Contains(ReferenceData.Instruction, result.Prompt);
        Assert.True(result.Prompt.IndexOf("REFERENCE DATA", StringComparison.Ordinal)
            < result.Prompt.IndexOf("TEMPLATE", StringComparison.Ordinal));
        var templateSection = result.Prompt[result.Prompt.IndexOf("TEMPLATE", StringComparison.Ordinal)..];
        Assert.DoesNotContain("| Monday |", templateSection);
    }

    [Fact]
    public async Task One_section_per_source_when_a_placeholder_synthesises_several_inputs()
    {
        var template = ForecastTemplate();
        template["variables"] = new JsonObject
        {
            ["examResults"] = new JsonArray { new JsonObject { ["subject"] = "Maths", ["averageScore"] = 58 } },
            ["attendance"] = new JsonArray { new JsonObject { ["term"] = "Term 1", ["attendancePct"] = 91 } },
            ["surveyResults"] = new JsonArray { new JsonObject { ["question"] = "I feel supported", ["agreePct"] = 64 } },
        };
        template["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "placeholder",
                ["instructionType"] = "summary",
                ["config"] = new JsonObject
                {
                    ["description"] = "Recommend improvements using the reference data",
                    ["dataSource"] = new JsonArray { "examResults", "attendance", "surveyResults" },
                },
            },
        };

        var result = await new ItsCompiler().CompileAsync(template);

        var exams = result.Prompt.IndexOf("### examResults", StringComparison.Ordinal);
        var attendance = result.Prompt.IndexOf("### attendance", StringComparison.Ordinal);
        var survey = result.Prompt.IndexOf("### surveyResults", StringComparison.Ordinal);
        Assert.True(exams > -1 && attendance > exams && survey > attendance);
        Assert.Contains("| Maths | 58 |", result.Prompt);
    }

    [Fact]
    public async Task Data_limit_caps_items_and_max_wins_across_placeholders()
    {
        var template = ForecastTemplate();
        var content = (JsonArray)template["content"]!;
        ((JsonObject)((JsonObject)content[1]!)["config"]!)["dataLimit"] = 1;
        content.Add(new JsonObject
        {
            ["type"] = "placeholder",
            ["instructionType"] = "summary",
            ["config"] = new JsonObject
            {
                ["description"] = "More from the forecast reference data",
                ["dataSource"] = "forecast",
                ["dataLimit"] = 2,
            },
        });

        var result = await new ItsCompiler().CompileAsync(template);

        Assert.Contains("Showing the first 2 of 3 items.", result.Prompt);
        Assert.Equal(1, CountOccurrences(result.Prompt, "### forecast"));
        Assert.DoesNotContain("| Sunday |", result.Prompt);
    }

    [Fact]
    public async Task Unknown_source_fails_compilation()
    {
        var template = ForecastTemplate();
        ((JsonObject)((JsonObject)((JsonArray)template["content"]!)[1]!)["config"]!)["dataSource"] = "missing";

        var error = await Assert.ThrowsAsync<ItsCompilationException>(() => new ItsCompiler().CompileAsync(template));
        Assert.Contains("Unknown data source 'missing'", error.Message);
    }

    [Fact]
    public void Render_helpers_match_reference_compilers()
    {
        Assert.Equal("- a\n- b", ReferenceData.RenderDataSource(new JsonArray { "a", "b" }));
        Assert.Equal(
            "| Field | Value |\n| --- | --- |\n| name | x |\n| count | 2 |",
            ReferenceData.RenderDataSource(new JsonObject { ["name"] = "x", ["count"] = 2 }));
        Assert.Equal(
            "| a | note |\n| --- | --- |\n| {\"b\":1} | x\\|y |",
            ReferenceData.RenderDataSource(new JsonArray
            {
                new JsonObject { ["a"] = new JsonObject { ["b"] = 1 }, ["note"] = "x|y" },
            }));
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
