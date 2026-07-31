using System.Text.Json.Nodes;
using Xunit;

namespace Its.Compiler.Tests;

/// <summary>
/// Direct unit coverage of template-structure validation, input hardening
/// and schema URL security, asserting the specific rule that fired rather
/// than relying on the corpus catch-all tests.
/// </summary>
public class ValidationAndSecurityTests
{
    private static JsonObject TextTemplate(string text) => new()
    {
        ["version"] = "1.0.0",
        ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } },
    };

    [Fact]
    public async Task Missing_version_is_a_validation_error()
    {
        var template = new JsonObject
        {
            ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = "hi" } },
        };
        var error = await Assert.ThrowsAsync<ItsValidationException>(() => new ItsCompiler().CompileAsync(template));
        Assert.Contains("version", error.Message);
    }

    [Fact]
    public async Task Empty_content_is_a_validation_error()
    {
        var template = new JsonObject { ["version"] = "1.0.0", ["content"] = new JsonArray() };
        var error = await Assert.ThrowsAsync<ItsValidationException>(() => new ItsCompiler().CompileAsync(template));
        Assert.Contains("content must not be empty", error.Message);
    }

    [Fact]
    public async Task Placeholder_without_description_is_a_validation_error()
    {
        var template = new JsonObject
        {
            ["version"] = "1.0.0",
            ["customInstructionTypes"] = new JsonObject
            {
                ["summary"] = new JsonObject { ["template"] = "<<Summarise: ([{<{description}>}]).>>" },
            },
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "placeholder",
                    ["instructionType"] = "summary",
                    ["config"] = new JsonObject(),
                },
            },
        };
        var error = await Assert.ThrowsAsync<ItsValidationException>(() => new ItsCompiler().CompileAsync(template));
        Assert.Contains("missing its required description", error.Message);
    }

    [Fact]
    public async Task Unknown_element_type_is_a_validation_error()
    {
        var template = new JsonObject
        {
            ["version"] = "1.0.0",
            ["content"] = new JsonArray { new JsonObject { ["type"] = "mystery" } },
        };
        var error = await Assert.ThrowsAsync<ItsValidationException>(() => new ItsCompiler().CompileAsync(template));
        Assert.Contains("Unknown content element type: 'mystery'", error.Message);
    }

    [Fact]
    public async Task Dangerous_variable_names_are_blocked_by_name()
    {
        var template = TextTemplate("hi");
        template["variables"] = new JsonObject { ["constructor"] = "x" };
        var error = await Assert.ThrowsAsync<ItsVariableException>(() => new ItsCompiler().CompileAsync(template));
        Assert.Contains("Dangerous variable name", error.Message);
        Assert.Contains("'constructor'", error.Message);
    }

    [Fact]
    public async Task Dangerous_text_content_is_blocked_with_the_context()
    {
        var error = await Assert.ThrowsAsync<ItsSecurityException>(
            () => new ItsCompiler().CompileAsync(TextTemplate("<script>alert(1)</script>")));
        Assert.Contains("Dangerous content pattern detected in text element", error.Message);
    }

    [Fact]
    public async Task Null_bytes_are_blocked()
    {
        var error = await Assert.ThrowsAsync<ItsSecurityException>(
            () => new ItsCompiler().CompileAsync(TextTemplate("hi\0there")));
        Assert.Contains("Null byte detected", error.Message);
    }

    private static JsonObject ExtendsTemplate(string schemaUrl) => new()
    {
        ["version"] = "1.0.0",
        ["extends"] = new JsonArray { schemaUrl },
        ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = "hi" } },
    };

    [Theory]
    [InlineData("https://localhost/schema.json", "Localhost schema access blocked")]
    [InlineData("https://127.0.0.1/schema.json", "Localhost schema access blocked")]
    [InlineData("https://192.168.1.10/schema.json", "Private network schema access blocked")]
    [InlineData("https://example.com/schema.json", "not in allowlist")]
    [InlineData("http://alexanderparker.github.io/schema.json", "HTTP schema URLs are not allowed")]
    public async Task Blocked_schema_urls_name_the_rule(string schemaUrl, string expectedMessage)
    {
        var error = await Assert.ThrowsAsync<ItsSecurityException>(
            () => new ItsCompiler().CompileAsync(ExtendsTemplate(schemaUrl)));
        Assert.Contains(expectedMessage, error.Message);
    }

    [Fact]
    public async Task Dangerous_extends_protocols_are_rejected_at_input_validation()
    {
        // ftp:// and absolute file:// never reach the schema loader; the
        // input validator rejects the extends entry outright
        var ftp = await Assert.ThrowsAnyAsync<ItsCompilationException>(
            () => new ItsCompiler().CompileAsync(ExtendsTemplate("ftp://example.com/schema.json")));
        Assert.Contains("Invalid extension URL", ftp.Message);

        var file = await Assert.ThrowsAnyAsync<ItsCompilationException>(
            () => new ItsCompiler().CompileAsync(ExtendsTemplate("file:///C:/schemas/types.json")));
        Assert.Contains("Invalid extension URL", file.Message);
    }

    [Fact]
    public async Task Relative_file_schemas_are_disabled_by_default_with_a_named_rule()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "json-types-template.json");
        var error = await Assert.ThrowsAsync<ItsSecurityException>(
            () => new ItsCompiler().CompileFileAsync(fixturePath));
        Assert.Contains("Local file schemas are disabled", error.Message);
    }
}

public class CompilerOptionsEnvironmentTests
{
    private static void WithEnvironment(Dictionary<string, string?> values, Action action)
    {
        var saved = new Dictionary<string, string?>();
        foreach (var pair in values)
        {
            saved[pair.Key] = Environment.GetEnvironmentVariable(pair.Key);
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
        try
        {
            action();
        }
        finally
        {
            foreach (var pair in saved)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }

    [Fact]
    public void FromEnvironment_reads_every_its_limit_variable()
    {
        WithEnvironment(new Dictionary<string, string?>
        {
            ["ITS_MAX_TEMPLATE_SIZE"] = "2048",
            ["ITS_MAX_CONTENT_ELEMENTS"] = "77",
            ["ITS_MAX_NESTING_DEPTH"] = "6",
            ["ITS_MAX_VARIABLE_COUNT"] = "123",
            ["ITS_MAX_VARIABLE_ARRAY_ITEMS"] = "45",
            ["ITS_MAX_TEXT_LENGTH"] = "67",
            ["ITS_ALLOW_HTTP"] = "true",
            ["ITS_ALLOW_LOCAL_SCHEMAS"] = "true",
        }, () =>
        {
            var options = CompilerOptions.FromEnvironment();
            Assert.Equal(2048, options.MaxTemplateSize);
            Assert.Equal(77, options.MaxContentElements);
            Assert.Equal(6, options.MaxNestingDepth);
            Assert.Equal(123, options.MaxVariableCount);
            Assert.Equal(45, options.MaxVariableArrayItems);
            Assert.Equal(67, options.MaxTextLength);
            Assert.True(options.AllowHttp);
            Assert.True(options.AllowLocalFileSchemas);
        });
    }

    [Fact]
    public void FromEnvironment_defaults_survive_unset_and_junk_values()
    {
        WithEnvironment(new Dictionary<string, string?>
        {
            ["ITS_MAX_VARIABLE_COUNT"] = "not-a-number",
            ["ITS_ALLOW_HTTP"] = null,
        }, () =>
        {
            var options = CompilerOptions.FromEnvironment();
            var defaults = new CompilerOptions();
            Assert.Equal(defaults.MaxVariableCount, options.MaxVariableCount);
            Assert.False(options.AllowHttp);
        });
    }
}
