using System.Text.Json.Nodes;
using Xunit;

namespace InstructionTemplateSpecification.Compiler.Tests;

public class CollectionFunctionTests
{
    private static JsonObject Template(string text) => new()
    {
        ["version"] = "1.0.0",
        ["variables"] = new JsonObject
        {
            ["forecast"] = new JsonArray
            {
                new JsonObject { ["day"] = "Monday", ["high"] = 24, ["wet"] = false },
                new JsonObject { ["day"] = "Tuesday", ["high"] = 31, ["wet"] = true },
                new JsonObject { ["day"] = "Wednesday", ["high"] = 27, ["wet"] = false },
            },
            ["tags"] = new JsonArray { "solar", "garden", "lantern" },
            ["scores"] = new JsonArray { 2, 4, 9 },
        },
        ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } },
    };

    private static async Task<string> CompileTextAsync(string text)
    {
        var result = await new ItsCompiler().CompileAsync(Template(text));
        return result.Prompt[result.Prompt.IndexOf("TEMPLATE", StringComparison.Ordinal)..];
    }

    [Theory]
    [InlineData("${forecast.concat(day)}", "Monday, Tuesday, Wednesday")]
    [InlineData("${tags.concat()}", "solar, garden, lantern")]
    [InlineData("${forecast.sum(high)}", "82")]
    [InlineData("${scores.sum()}", "15")]
    [InlineData("${scores.avg()}", "5")]
    [InlineData("${forecast.min(high)}", "24")]
    [InlineData("${forecast.max(high)}", "31")]
    [InlineData("${forecast.top(2).concat(day)}", "Monday, Tuesday")]
    [InlineData("${tags.top(1).concat()}", "solar")]
    [InlineData("${forecast.concat(wet)}", "false, true, false")]
    public async Task Functions_evaluate(string reference, string expected)
    {
        Assert.Contains(expected, await CompileTextAsync(reference));
    }

    [Theory]
    [InlineData("${forecast[0].sum(high)}", "sum() requires an array")]
    [InlineData("${forecast.sum(missing)}", "Property 'missing' not found on every item")]
    [InlineData("${forecast.sum(day)}", "sum() requires numeric values")]
    [InlineData("${forecast.top(x)}", "top() requires a non-negative integer")]
    [InlineData("${forecast.concat(day).sum()}", "sum() requires an array")]
    public async Task Invalid_usages_fail(string reference, string expectedMessage)
    {
        var error = await Assert.ThrowsAnyAsync<ItsCompilationException>(() => CompileTextAsync(reference));
        Assert.Contains(expectedMessage, error.Message);
    }
}
