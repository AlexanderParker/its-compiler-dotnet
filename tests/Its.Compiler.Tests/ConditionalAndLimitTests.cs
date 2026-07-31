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
        // Exactly one branch must be emitted: a false condition emits the
        // else content, not nothing
        Assert.True(result.Prompt.Contains("YES") ^ result.Prompt.Contains("NO"));
        return result.Prompt.Contains("YES");
    }

    [Theory]
    [InlineData("a == true && b > 3", true)]
    [InlineData("a == true and b > 3", true)]
    [InlineData("!a || b == 10", false)]
    [InlineData("not a or b == 5", true)]
    [InlineData("b != 4", true)]
    [InlineData("b != 5", false)]
    [InlineData("1 < b <= 5", true)]
    [InlineData("5 < b < 3", false)]
    [InlineData("name == 'orders'", true)]
    [InlineData("name in ['orders', 'invoices']", true)]
    [InlineData("name not in ['orders', 'invoices']", false)]
    [InlineData("'xyz' not in name", true)]
    [InlineData("'ord' in name", true)]
    [InlineData("-b < 0", true)]
    [InlineData("items.length == 2", true)]
    [InlineData("items[0] == 'first'", true)]
    [InlineData("items[-1] == 'second'", true)]
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
        var error = await Assert.ThrowsAsync<ItsVariableException>(() => strict.CompileAsync(template));
        Assert.Contains("Too many variables", error.Message);
        Assert.Contains("(max: 50)", error.Message);

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
        var arrayError = await Assert.ThrowsAsync<ItsVariableException>(
            () => arrayLimited.CompileAsync((JsonObject)template.DeepClone()));
        Assert.Contains("Array too large", arrayError.Message);

        var textLimited = new ItsCompiler(new CompilerOptions { MaxTextLength = 40 });
        var textError = await Assert.ThrowsAsync<ItsVariableException>(
            () => textLimited.CompileAsync((JsonObject)template.DeepClone()));
        Assert.Contains("String value too long", textError.Message);

        var permissive = await new ItsCompiler().CompileAsync((JsonObject)template.DeepClone());
        Assert.Contains("hello", permissive.Prompt);
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
