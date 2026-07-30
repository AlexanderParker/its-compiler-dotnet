using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Its.Compiler;

/// <summary>
/// Resolves ${variable} references (dot properties, [index] access including
/// negative indices, and .length) and substitutes them into content, with
/// configurable payload limits.
/// </summary>
internal sealed partial class VariableProcessor
{
    private readonly CompilerOptions _options;

    [GeneratedRegex(@"\$\{([^}]+)\}")]
    private static partial Regex VariablePattern();

    public VariableProcessor(CompilerOptions options)
    {
        _options = options;
    }

    public void ValidateVariables(JsonObject variables)
    {
        var total = CountTotalVariables(variables, 0);
        if (total > _options.MaxVariableCount)
        {
            throw new ItsVariableException($"Too many variables: {total} (max: {_options.MaxVariableCount})");
        }
        ValidateNode(variables, "", 0);
    }

    private int CountTotalVariables(JsonNode? node, int depth)
    {
        if (depth > _options.MaxNestingDepth) return 0;
        return node switch
        {
            JsonObject obj => obj.Count + obj.Sum(pair => CountTotalVariables(pair.Value, depth + 1)),
            JsonArray array => array.Sum(item => CountTotalVariables(item, depth + 1)),
            _ => 0,
        };
    }

    private void ValidateNode(JsonNode? node, string path, int depth)
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
                    ValidateNode(pair.Value, path.Length == 0 ? pair.Key : $"{path}.{pair.Key}", depth + 1);
                }
                break;
            case JsonArray array:
                if (array.Count > _options.MaxVariableArrayItems)
                {
                    throw new ItsVariableException($"Array too large at {path}: {array.Count} items");
                }
                for (var i = 0; i < array.Count; i++)
                {
                    ValidateNode(array[i], $"{path}[{i}]", depth + 1);
                }
                break;
            case JsonValue value when value.TryGetValue<string>(out var text):
                if (text.Length > _options.MaxTextLength)
                {
                    throw new ItsVariableException($"String value too long at {path}: {text.Length} chars");
                }
                break;
        }
    }

    /// <summary>Substitutes ${refs} throughout a content tree, returning a processed copy.</summary>
    public JsonArray ProcessContent(JsonArray content, JsonObject variables)
    {
        var processed = new JsonArray();
        foreach (var element in content)
        {
            processed.Add(ProcessNode(element?.DeepClone(), variables));
        }
        return processed;
    }

    private JsonNode? ProcessNode(JsonNode? node, JsonObject variables)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(pair => pair.Key).ToList())
                {
                    obj[key] = ProcessNode(obj[key], variables);
                }
                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var item = array[i];
                    array[i] = ProcessNode(item?.DeepClone(), variables);
                }
                return array;
            case JsonValue value when value.TryGetValue<string>(out var text):
                return JsonValue.Create(SubstituteReferences(text, variables));
            default:
                return node;
        }
    }

    public string SubstituteReferences(string text, JsonObject variables)
    {
        return VariablePattern().Replace(text, match =>
        {
            var reference = match.Groups[1].Value.Trim();
            var resolved = ResolveReference(reference, variables);
            return ConvertToString(resolved);
        });
    }

    private string ConvertToString(JsonNode? value)
    {
        switch (value)
        {
            case null:
                return "";
            case JsonArray array:
                return string.Join(", ", array.Select(item => ConvertToString(item)));
            case JsonObject obj:
                return $"[Object with {obj.Count} properties]";
            case JsonValue scalar:
                if (scalar.TryGetValue<string>(out var text))
                {
                    return text.Length > _options.MaxTextLength
                        ? text[.._options.MaxTextLength] + "... [TRUNCATED]"
                        : text;
                }
                if (scalar.TryGetValue<bool>(out var flag)) return flag ? "true" : "false";
                return scalar.ToJsonString();
            default:
                return value.ToJsonString();
        }
    }

    /// <summary>Resolves a reference like user.name, items[0].sku or features.length.</summary>
    public JsonNode? ResolveReference(string reference, JsonObject variables)
    {
        var wantsLength = false;
        var path = reference;
        if (path.EndsWith(".length", StringComparison.Ordinal))
        {
            wantsLength = true;
            path = path[..^".length".Length];
        }

        JsonNode? current = variables;
        foreach (var part in ParsePath(path, reference))
        {
            if (part.Index is int index)
            {
                if (current is not JsonArray array)
                {
                    throw new ItsVariableException($"Cannot index non-array value in reference '{reference}'");
                }
                var actual = index < 0 ? array.Count + index : index;
                if (actual < 0 || actual >= array.Count)
                {
                    throw new ItsVariableException($"Array index {index} out of bounds in reference '{reference}'");
                }
                current = array[actual];
            }
            else
            {
                if (current is not JsonObject obj || !obj.TryGetPropertyValue(part.Name!, out var next))
                {
                    throw new ItsVariableException($"Undefined variable reference: '{reference}'");
                }
                current = next;
            }
        }

        if (wantsLength)
        {
            return current switch
            {
                JsonArray array => JsonValue.Create(array.Count),
                JsonValue value when value.TryGetValue<string>(out var text) => JsonValue.Create(text.Length),
                _ => throw new ItsVariableException($"Cannot take .length of value in reference '{reference}'"),
            };
        }
        return current;
    }

    private readonly record struct PathPart(string? Name, int? Index);

    private static IEnumerable<PathPart> ParsePath(string path, string reference)
    {
        var pattern = PathPattern().Match(path);
        if (!pattern.Success)
        {
            throw new ItsVariableException($"Malformed variable reference: '{reference}'");
        }
        foreach (Match token in PathTokenPattern().Matches(path))
        {
            if (token.Groups["name"].Success)
            {
                yield return new PathPart(token.Groups["name"].Value, null);
            }
            else
            {
                yield return new PathPart(null, int.Parse(token.Groups["index"].Value));
            }
        }
    }

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*(\.[a-zA-Z_][a-zA-Z0-9_]*|\[-?\d+\])*$")]
    private static partial Regex PathPattern();

    [GeneratedRegex(@"(?<name>[a-zA-Z_][a-zA-Z0-9_]*)|\[(?<index>-?\d+)\]")]
    private static partial Regex PathTokenPattern();
}
