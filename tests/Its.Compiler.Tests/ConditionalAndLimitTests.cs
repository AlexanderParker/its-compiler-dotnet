using System.Text.Json.Nodes;
using Xunit;

namespace Its.Compiler.Tests;

public class ConditionalAndLimitTests
{
    private static JsonObject TemplateWithCondition(string condition, JsonObject variables) => new()
    {
        ["version"] = "1.0.0",
        ["variables"] = variables,
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "conditional",
                ["condition"] = condition,
                ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = "YES" } },
                ["else"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = "NO" } },
            },
        },
    };

    private static async Task<bool> Evaluates(string condition, JsonObject variables)
    {
        var result = await new ItsCompiler().CompileAsync(TemplateWithCondition(condition, variables));
        return result.Prompt.Contains("YES");
    }

    [Theory]
    [InlineData("a == true && b > 3", true)]
    [InlineData("a == true and b > 3", true)]
    [InlineData("!a || b == 10", false)]
    [InlineData("not a or b == 5", true)]
    [InlineData("1 < b <= 5", true)]
    [InlineData("name == 'orders'", true)]
    [InlineData("name in ['orders', 'invoices']", true)]
    [InlineData("'xyz' not in name", true)]
    [InlineData("-b < 0", true)]
    [InlineData("items.length == 2", true)]
    [InlineData("items[0] == 'first'", true)]
    [InlineData("settings.enabled == true", true)]
    public async Task Spec_operators_evaluate(string condition, bool expected)
    {
        var variables = new JsonObject
        {
            ["a"] = true,
            ["b"] = 5,
            ["name"] = "orders",
            ["items"] = new JsonArray { "first", "second" },
            ["settings"] = new JsonObject { ["enabled"] = true },
        };
        Assert.Equal(expected, await Evaluates(condition, variables));
    }

    [Fact]
    public async Task Variable_count_limit_is_configurable()
    {
        var rows = new JsonArray();
        for (var i = 0; i < 60; i++) rows.Add(new JsonObject { ["n"] = i });
        var template = new JsonObject
        {
            ["version"] = "1.0.0",
            ["variables"] = new JsonObject { ["rows"] = rows },
            ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = "hello" } },
        };

        var strict = new ItsCompiler(new CompilerOptions { MaxVariableCount = 50 });
        await Assert.ThrowsAsync<ItsVariableException>(() => strict.CompileAsync(template));

        var permissive = await new ItsCompiler().CompileAsync(template);
        Assert.Contains("hello", permissive.Prompt);
    }

    [Fact]
    public async Task Array_and_text_limits_are_configurable()
    {
        var template = new JsonObject
        {
            ["version"] = "1.0.0",
            ["variables"] = new JsonObject
            {
                ["items"] = new JsonArray { 1, 2, 3 },
                ["note"] = new string('x', 50),
            },
            ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = "hello" } },
        };

        var arrayLimited = new ItsCompiler(new CompilerOptions { MaxVariableArrayItems = 2 });
        await Assert.ThrowsAsync<ItsVariableException>(() => arrayLimited.CompileAsync((JsonObject)template.DeepClone()));

        var textLimited = new ItsCompiler(new CompilerOptions { MaxTextLength = 40 });
        await Assert.ThrowsAsync<ItsVariableException>(() => textLimited.CompileAsync((JsonObject)template.DeepClone()));
    }

    [Fact]
    public async Task Variable_substitution_supports_paths_indices_and_length()
    {
        var template = new JsonObject
        {
            ["version"] = "1.0.0",
            ["variables"] = new JsonObject
            {
                ["product"] = new JsonObject { ["name"] = "Lantern", ["price"] = 39.5 },
                ["features"] = new JsonArray { "solar", "waterproof" },
            },
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = "${product.name} at ${product.price}: ${features} (${features.length}); first=${features[0]}, last=${features[-1]}",
                },
            },
        };

        var result = await new ItsCompiler().CompileAsync(template);

        Assert.Contains("Lantern at 39.5: solar, waterproof (2); first=solar, last=waterproof", result.Prompt);
    }
}
