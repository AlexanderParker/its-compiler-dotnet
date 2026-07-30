using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Its.Compiler;

/// <summary>
/// Structural and content validation matching the Python reference
/// compiler's input validator: element structure, dangerous content
/// patterns, variable name hardening, custom type checks and extends
/// entry validation.
/// </summary>
internal sealed partial class InputValidator
{
    private static readonly string[] AllowedElementTypes = { "text", "placeholder", "conditional" };

    private static readonly HashSet<string> DangerousVariableNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "constructor", "prototype", "__proto__", "__class__", "__bases__",
        "eval", "exec", "function", "import", "global", "globals", "locals",
        "vars", "dir", "open", "input", "compile", "this", "window", "document", "process",
    };

    [GeneratedRegex(
        @"<script[^>]*>|javascript\s*:|data\s*:\s*text/html|\beval\s*\(|\bexec\s*\(|\bFunction\s*\(|" +
        @"\bsetTimeout\s*\(|\bsetInterval\s*\(|\bdocument\.\w+|\bwindow\.\w+|__proto__|__import__|" +
        @"\bsubprocess\b|\bos\.system\b|<iframe[^>]*>|\bon\w+\s*=",
        RegexOptions.IgnoreCase)]
    private static partial Regex DangerousContentPattern();

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*$")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.\-]*:")]
    private static partial Regex UrlSchemePattern();

    private readonly CompilerOptions _options;

    public InputValidator(CompilerOptions options)
    {
        _options = options;
    }

    public void ValidateTemplate(JsonObject template)
    {
        ValidateExtends(template["extends"]);
        ValidateCustomTypes(template["customInstructionTypes"]);
        if (template["content"] is JsonArray content)
        {
            ValidateContent(content, 0);
        }
    }

    private void ValidateContent(JsonArray content, int depth)
    {
        if (depth > _options.MaxNestingDepth)
        {
            throw new ItsSecurityException("Content nesting too deep");
        }
        foreach (var element in content)
        {
            if (element is not JsonObject obj)
            {
                throw new ItsValidationException("Content elements must be objects");
            }
            var type = (obj["type"] as JsonValue)?.GetValue<string>();
            if (type is null || !AllowedElementTypes.Contains(type))
            {
                throw new ItsValidationException($"Unknown content element type: '{type ?? "(missing)"}'");
            }
            switch (type)
            {
                case "text":
                    if (obj["text"] is not JsonValue textValue || !textValue.TryGetValue<string>(out var text))
                    {
                        throw new ItsValidationException("Text element is missing its text string");
                    }
                    ValidateText(text, "text element");
                    break;

                case "placeholder":
                    if (obj["instructionType"] is not JsonValue)
                    {
                        throw new ItsValidationException("Placeholder element is missing its instructionType");
                    }
                    if (obj["config"] is not JsonObject config)
                    {
                        throw new ItsValidationException("Placeholder element is missing its config object");
                    }
                    if (config["description"] is not JsonValue descriptionValue
                        || !descriptionValue.TryGetValue<string>(out var description))
                    {
                        throw new ItsValidationException("Placeholder config is missing its required description");
                    }
                    ValidateText(description, "placeholder description");
                    ValidateConfigValues(config, depth + 1);
                    break;

                case "conditional":
                    if (obj["condition"] is not JsonValue conditionValue
                        || !conditionValue.TryGetValue<string>(out var condition))
                    {
                        throw new ItsValidationException("Conditional element is missing its condition string");
                    }
                    ValidateConditionText(condition);
                    if (obj["content"] is not JsonArray branch)
                    {
                        throw new ItsValidationException("Conditional element is missing its content array");
                    }
                    ValidateContent(branch, depth + 1);
                    if (obj["else"] is JsonArray elseBranch)
                    {
                        ValidateContent(elseBranch, depth + 1);
                    }
                    break;
            }
        }
    }

    private void ValidateConfigValues(JsonObject config, int depth)
    {
        if (depth > _options.MaxNestingDepth) return;
        foreach (var pair in config)
        {
            switch (pair.Value)
            {
                case JsonValue value when value.TryGetValue<string>(out var text):
                    ValidateText(text, $"config value '{pair.Key}'");
                    break;
                case JsonObject nested:
                    ValidateConfigValues(nested, depth + 1);
                    break;
            }
        }
    }

    public void ValidateText(string text, string context)
    {
        if (text.Length > _options.MaxTextLength)
        {
            throw new ItsSecurityException(
                $"Text content too long in {context}: {text.Length} characters (max: {_options.MaxTextLength})");
        }
        if (text.Contains('\0'))
        {
            throw new ItsSecurityException($"Null byte detected in {context}");
        }
        if (_options.EnableContentScanning && DangerousContentPattern().IsMatch(text))
        {
            throw new ItsSecurityException($"Dangerous content pattern detected in {context}");
        }
    }

    private void ValidateConditionText(string condition)
    {
        if (condition.Length > _options.MaxExpressionLength)
        {
            throw new ItsSecurityException($"Condition too long: {condition.Length} characters");
        }
        if (_options.EnableContentScanning
            && (DangerousContentPattern().IsMatch(condition) || condition.Contains("__", StringComparison.Ordinal)))
        {
            throw new ItsSecurityException("Dangerous pattern detected in conditional expression");
        }
    }

    public void ValidateVariables(JsonObject variables)
    {
        ValidateVariableObject(variables, "", 0);
    }

    private void ValidateVariableObject(JsonNode? node, string path, int depth)
    {
        if (depth > _options.MaxNestingDepth)
        {
            throw new ItsVariableException($"Variable nesting too deep at {path}");
        }
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj)
                {
                    ValidateVariableName(pair.Key, path);
                    ValidateVariableObject(pair.Value, path.Length == 0 ? pair.Key : $"{path}.{pair.Key}", depth + 1);
                }
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    ValidateVariableObject(array[i], $"{path}[{i}]", depth + 1);
                }
                break;
            case JsonValue value when value.TryGetValue<string>(out var text):
                ValidateText(text, $"variable {path}");
                break;
        }
    }

    private void ValidateVariableName(string name, string path)
    {
        if (name.Length > 100)
        {
            throw new ItsVariableException($"Variable name too long at {path}: {name.Length} chars");
        }
        if (!IdentifierPattern().IsMatch(name))
        {
            throw new ItsVariableException($"Invalid variable name at {path}: '{name}'");
        }
        if (DangerousVariableNames.Contains(name) || name.StartsWith("__", StringComparison.Ordinal))
        {
            throw new ItsVariableException($"Dangerous variable name at {path}: '{name}'");
        }
    }

    private void ValidateExtends(JsonNode? extends)
    {
        if (extends is null) return;
        if (extends is not JsonArray list)
        {
            throw new ItsValidationException("extends must be an array");
        }
        if (list.Count > _options.MaxExtends)
        {
            throw new ItsSecurityException($"Too many extensions: {list.Count}");
        }
        foreach (var entry in list)
        {
            if (entry is not JsonValue value || !value.TryGetValue<string>(out var url))
            {
                throw new ItsValidationException("extends entries must be strings");
            }
            // Scheme-less references are relative to the template and are
            // validated after resolution; explicit non-http schemes and
            // protocol-relative URLs are rejected here.
            if (url.StartsWith("http://", StringComparison.Ordinal)
                || url.StartsWith("https://", StringComparison.Ordinal))
            {
                continue;
            }
            if (UrlSchemePattern().IsMatch(url) || url.StartsWith("//", StringComparison.Ordinal))
            {
                throw new ItsSecurityException($"Invalid extension URL: {url}");
            }
        }
    }

    private void ValidateCustomTypes(JsonNode? customTypes)
    {
        if (customTypes is null) return;
        if (customTypes is not JsonObject types)
        {
            throw new ItsValidationException("customInstructionTypes must be an object");
        }
        if (types.Count > _options.MaxCustomTypes)
        {
            throw new ItsSecurityException($"Too many custom instruction types: {types.Count}");
        }
        foreach (var pair in types)
        {
            if (!IdentifierPattern().IsMatch(pair.Key.Replace("-", "_")))
            {
                throw new ItsValidationException($"Invalid custom instruction type name: '{pair.Key}'");
            }
            // Unwrapped templates are valid: the compiler applies the
            // instruction wrapper when the template lacks << >> brackets
            if (pair.Value is not JsonObject definition
                || definition["template"] is not JsonValue templateValue
                || !templateValue.TryGetValue<string>(out _))
            {
                throw new ItsValidationException($"Custom instruction type '{pair.Key}' is missing its template string");
            }
        }
    }
}
