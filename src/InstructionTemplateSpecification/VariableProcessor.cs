using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace InstructionTemplateSpecification;

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

    /// <summary>
    /// Substitutes ${refs} throughout a content tree, returning a processed
    /// copy. When objectReferences is provided, references resolving to
    /// objects substitute a pointer ("the settings reference data") and the
    /// object is collected for the REFERENCE DATA section.
    /// </summary>
    public JsonArray ProcessContent(JsonArray content, JsonObject variables, Dictionary<string, JsonNode?>? objectReferences = null)
    {
        var processed = new JsonArray();
        foreach (var element in content)
        {
            processed.Add(ProcessNode(element?.DeepClone(), variables, objectReferences));
        }
        return processed;
    }

    private JsonNode? ProcessNode(JsonNode? node, JsonObject variables, Dictionary<string, JsonNode?>? objectReferences)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(pair => pair.Key).ToList())
                {
                    // Condition strings never carry object pointers
                    var collector = key == "condition" ? null : objectReferences;
                    obj[key] = ProcessNode(obj[key], variables, collector);
                }
                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var item = array[i];
                    array[i] = ProcessNode(item?.DeepClone(), variables, objectReferences);
                }
                return array;
            case JsonValue value when value.TryGetValue<string>(out var text):
                return JsonValue.Create(SubstituteReferences(text, variables, objectReferences));
            default:
                return node;
        }
    }

    public string SubstituteReferences(string text, JsonObject variables, Dictionary<string, JsonNode?>? objectReferences = null)
    {
        return VariablePattern().Replace(text, match =>
        {
            var reference = match.Groups[1].Value.Trim();
            var resolved = ResolveReference(reference, variables);
            if (objectReferences is not null && resolved is JsonObject)
            {
                objectReferences[reference] = resolved;
                return $"the {reference} reference data";
            }
            return ConvertToString(resolved);
        });
    }

    private string ConvertToString(JsonNode? value)
    {
        switch (value)
        {
            case null:
                // "null", not an empty string. An empty string would lose the
                // distinction between a null and a genuinely empty value, and
                // the other two compilers render "null".
                return "null";
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
                return RenderScalar(scalar);
            default:
                return RenderScalar(value);
        }
    }

    /// <summary>
    /// Renders a scalar exactly as the Python and JavaScript compilers do.
    /// </summary>
    /// <remarks>
    /// Each implementation used to stringify with its own language's
    /// conventions, which agreed on common values and diverged at the edges.
    /// This is the single definition the three now share: lowercase booleans,
    /// "null" for null, whole numbers without a decimal part, and exponents
    /// written with a lowercase e, no plus sign and no leading zeros.
    /// </remarks>
    internal static string RenderScalar(JsonNode? node)
    {
        if (node is null) return "null";

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text)) return text;

            switch (value.GetValueKind())
            {
                case System.Text.Json.JsonValueKind.True:
                    return "true";
                case System.Text.Json.JsonValueKind.False:
                    return "false";
                case System.Text.Json.JsonValueKind.Null:
                    return "null";
            }

            if (value.TryGetValue<double>(out var number))
            {
                return RenderNumber(number);
            }
        }

        return node.ToJsonString();
    }

    /// <summary>Renders a number in the form all three compilers agree on.</summary>
    internal static string RenderNumber(double value)
    {
        // "R" gives the shortest representation that round-trips. Invariant
        // culture because a comma decimal separator would be a locale-
        // dependent quirk of exactly the kind this is here to remove.
        var text = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        var exponentAt = text.IndexOfAny(['e', 'E']);
        if (exponentAt < 0)
        {
            // Whole values carry no decimal part: 1, never 1.0.
            if (text.EndsWith(".0", StringComparison.Ordinal))
            {
                text = text[..^2];
            }
            return text;
        }

        var mantissa = text[..exponentAt];
        var exponent = text[(exponentAt + 1)..];
        var negative = exponent.StartsWith('-');
        var digits = exponent.TrimStart('+', '-').TrimStart('0');
        if (digits.Length == 0) digits = "0";

        if (mantissa.EndsWith(".0", StringComparison.Ordinal))
        {
            mantissa = mantissa[..^2];
        }

        return $"{mantissa}e{(negative ? "-" : string.Empty)}{digits}";
    }

    /// <summary>Resolves a reference like user.name, items[0].sku, features.length or forecast.top(3).concat(day).</summary>
    public JsonNode? ResolveReference(string reference, JsonObject variables)
    {
        // Collection functions are a suffix chain applied after path resolution
        var calls = new List<(string Name, string? Arg)>();
        var baseReference = reference;
        for (var match = FunctionSuffixPattern().Match(baseReference); match.Success;
             match = FunctionSuffixPattern().Match(baseReference))
        {
            calls.Insert(0, (match.Groups[2].Value, match.Groups[3].Success ? match.Groups[3].Value : null));
            baseReference = match.Groups[1].Value;
        }
        if (calls.Count > 0)
        {
            var value = ResolveReference(baseReference, variables);
            foreach (var call in calls)
            {
                value = ApplyCollectionFunction(value, call.Name, call.Arg, reference);
            }
            return value;
        }

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

    [GeneratedRegex(@"^(.*)\.(concat|sum|avg|min|max|top)\(\s*([A-Za-z_][A-Za-z0-9_]*|\d+)?\s*\)$")]
    private static partial Regex FunctionSuffixPattern();

    private static string ConcatText(JsonNode? item) => RenderScalar(item);

    private static JsonNode? ApplyCollectionFunction(JsonNode? value, string name, string? arg, string reference)
    {
        if (value is not JsonArray array)
        {
            throw new ItsVariableException($"Function {name}() requires an array in reference '{reference}'");
        }

        if (name == "top")
        {
            if (arg is null || !int.TryParse(arg, out var count) || count < 0)
            {
                throw new ItsVariableException($"top() requires a non-negative integer in reference '{reference}'");
            }
            var sliced = new JsonArray();
            foreach (var item in array.Take(count))
            {
                sliced.Add(item?.DeepClone());
            }
            return sliced;
        }

        var items = new List<JsonNode?>();
        foreach (var item in array)
        {
            if (arg is null)
            {
                if (item is JsonObject or JsonArray)
                {
                    throw new ItsVariableException(
                        $"Function {name}() requires a property name for object items in reference '{reference}'");
                }
                items.Add(item);
            }
            else
            {
                if (item is not JsonObject obj || !obj.TryGetPropertyValue(arg, out var extracted))
                {
                    throw new ItsVariableException($"Property '{arg}' not found on every item in reference '{reference}'");
                }
                items.Add(extracted);
            }
        }

        if (name == "concat")
        {
            return JsonValue.Create(string.Join(", ", items.Select(ConcatText)));
        }

        var numbers = new List<double>();
        foreach (var item in items)
        {
            if (item is not JsonValue number || number.GetValueKind() != System.Text.Json.JsonValueKind.Number)
            {
                throw new ItsVariableException($"Function {name}() requires numeric values in reference '{reference}'");
            }
            numbers.Add(double.Parse(number.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture));
        }

        if (name == "sum") return JsonValue.Create(numbers.Sum());
        if (numbers.Count == 0)
        {
            throw new ItsVariableException($"{name}() of an empty array in reference '{reference}'");
        }
        return name switch
        {
            "avg" => JsonValue.Create(numbers.Average()),
            "min" => JsonValue.Create(numbers.Min()),
            _ => JsonValue.Create(numbers.Max()),
        };
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
                // Invariant culture: an array index in a template is not a
                // locale-formatted number, and parsing it as one would make
                // path resolution depend on the machine's settings.
                yield return new PathPart(
                    null,
                    int.Parse(token.Groups["index"].Value, CultureInfo.InvariantCulture));
            }
        }
    }

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*(\.[a-zA-Z_][a-zA-Z0-9_]*|\[-?\d+\])*$")]
    private static partial Regex PathPattern();

    [GeneratedRegex(@"(?<name>[a-zA-Z_][a-zA-Z0-9_]*)|\[(?<index>-?\d+)\]")]
    private static partial Regex PathTokenPattern();
}
