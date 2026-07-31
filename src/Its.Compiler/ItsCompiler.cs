using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Its.Compiler;

/// <summary>
/// Compiles Instruction Template Specification (ITS) templates into
/// structured AI prompts: variable substitution, conditional evaluation,
/// instruction type resolution through extends, configSchema defaults,
/// reference data sections and configurable processing limits.
/// </summary>
public sealed partial class ItsCompiler
{
    private readonly CompilerOptions _options;
    private readonly SchemaLoader _schemaLoader;

    public ItsCompiler(CompilerOptions? options = null)
    {
        _options = options ?? new CompilerOptions();
        _schemaLoader = new SchemaLoader(_options);
    }

    /// <summary>Compiles a template file; relative extends resolve against its directory.</summary>
    public async Task<CompilationResult> CompileFileAsync(
        string path, JsonObject? variables = null, CancellationToken cancellationToken = default)
    {
        var text = await File.ReadAllTextAsync(path, cancellationToken);
        JsonObject template;
        try
        {
            template = JsonNode.Parse(text) as JsonObject
                ?? throw new ItsValidationException($"Template file is not a JSON object: {path}");
        }
        catch (System.Text.Json.JsonException error)
        {
            throw new ItsValidationException($"Invalid JSON in template file: {error.Message}");
        }
        var baseUrl = new Uri(Path.GetDirectoryName(Path.GetFullPath(path))! + Path.DirectorySeparatorChar).AbsoluteUri;
        return await CompileAsync(template, variables, baseUrl, cancellationToken);
    }

    /// <summary>Compiles a parsed template with optional external variables overriding template variables.</summary>
    public async Task<CompilationResult> CompileAsync(
        JsonObject template, JsonObject? variables = null, string? baseUrl = null,
        CancellationToken cancellationToken = default)
    {
        ValidateStructure(template);
        var inputValidator = new InputValidator(_options);
        inputValidator.ValidateTemplate(template);

        var variableProcessor = new VariableProcessor(_options);

        // Merge template variables with provided variables (provided win)
        var merged = template["variables"] as JsonObject ?? new JsonObject();
        merged = (JsonObject)merged.DeepClone();
        if (variables is not null)
        {
            foreach (var pair in variables)
            {
                merged[pair.Key] = pair.Value?.DeepClone();
            }
        }
        variableProcessor.ValidateVariables(merged);
        inputValidator.ValidateVariables(merged);

        var instructionTypes = await LoadInstructionTypesAsync(template, baseUrl, cancellationToken);

        var content = (JsonArray)template["content"]!.DeepClone();
        var objectReferences = new Dictionary<string, JsonNode?>();
        var processed = variableProcessor.ProcessContent(content, merged, objectReferences);

        var evaluator = new ConditionalEvaluator(_options, variableProcessor);
        var finalContent = EvaluateConditionals(processed, evaluator, merged);

        var prompt = GeneratePrompt(finalContent, instructionTypes, template, merged, objectReferences);
        return new CompilationResult { Prompt = prompt, Warnings = Array.Empty<string>() };
    }

    private void ValidateStructure(JsonObject template)
    {
        if (template["version"] is null)
        {
            throw new ItsValidationException("Template is missing the required version field");
        }
        if (template["content"] is not JsonArray content)
        {
            throw new ItsValidationException("Template is missing the required content array");
        }
        if (content.Count == 0)
        {
            throw new ItsValidationException("Template content must not be empty");
        }
        if (content.Count > _options.MaxContentElements)
        {
            throw new ItsSecurityException($"Too many content elements: {content.Count}");
        }
        var size = Encoding.UTF8.GetByteCount(template.ToJsonString());
        if (size > _options.MaxTemplateSize)
        {
            throw new ItsSecurityException($"Template too large: {size} bytes");
        }
    }

    private async Task<Dictionary<string, JsonObject>> LoadInstructionTypesAsync(
        JsonObject template, string? baseUrl, CancellationToken cancellationToken)
    {
        var types = new Dictionary<string, JsonObject>();
        if (template["extends"] is JsonArray extends)
        {
            foreach (var entry in extends)
            {
                var url = entry?.GetValue<string>()
                    ?? throw new ItsValidationException("extends entries must be strings");
                var schema = await _schemaLoader.LoadAsync(url, baseUrl, cancellationToken);
                MergeTypes(types, schema["instructionTypes"] as JsonObject);
            }
        }
        MergeTypes(types, template["customInstructionTypes"] as JsonObject);
        return types;
    }

    private static void MergeTypes(Dictionary<string, JsonObject> types, JsonObject? source)
    {
        if (source is null) return;
        foreach (var pair in source)
        {
            if (pair.Value is JsonObject definition)
            {
                // Complete override principle: later definitions replace earlier ones
                types[pair.Key] = definition;
            }
        }
    }

    private static JsonArray EvaluateConditionals(JsonArray content, ConditionalEvaluator evaluator, JsonObject variables)
    {
        var result = new JsonArray();
        foreach (var element in content)
        {
            if (element is JsonObject obj && obj["type"]?.GetValue<string>() == "conditional")
            {
                var condition = obj["condition"]?.GetValue<string>()
                    ?? throw new ItsConditionalException("Conditional element is missing its condition");
                var branch = evaluator.Evaluate(condition, variables)
                    ? obj["content"] as JsonArray
                    : obj["else"] as JsonArray;
                if (branch is not null)
                {
                    foreach (var nested in EvaluateConditionals(branch, evaluator, variables))
                    {
                        result.Add(nested?.DeepClone());
                    }
                }
            }
            else
            {
                result.Add(element?.DeepClone());
            }
        }
        return result;
    }

    [GeneratedRegex(@"\{([^}<][^}]*)\}")]
    private static partial Regex TemplateVariablePattern();

    private string FormatInstruction(JsonObject placeholder, Dictionary<string, JsonObject> instructionTypes)
    {
        var typeName = placeholder["instructionType"]?.GetValue<string>()
            ?? throw new ItsValidationException("Placeholder is missing its instructionType");
        if (!instructionTypes.TryGetValue(typeName, out var definition))
        {
            throw new ItsCompilationException($"Unknown instruction type: '{typeName}'");
        }
        var templateString = definition["template"]?.GetValue<string>()
            ?? throw new ItsSchemaException($"Instruction type '{typeName}' has no template string");
        var config = placeholder["config"] as JsonObject ?? new JsonObject();

        var formatted = templateString.Replace("{description}", config["description"]?.GetValue<string>() ?? "");

        // Merge configSchema defaults beneath the supplied config
        var substitutions = new Dictionary<string, JsonNode?>();
        if (definition["configSchema"] is JsonObject schema && schema["properties"] is JsonObject properties)
        {
            foreach (var pair in properties)
            {
                if (pair.Value is JsonObject property && property.TryGetPropertyValue("default", out var fallback))
                {
                    substitutions[pair.Key] = fallback;
                }
            }
        }
        foreach (var pair in config)
        {
            if (pair.Key != "description") substitutions[pair.Key] = pair.Value;
        }

        foreach (var pair in substitutions)
        {
            var rendered = pair.Value switch
            {
                JsonValue value when value.TryGetValue<bool>(out var flag) => flag ? "true" : "false",
                JsonValue value when value.TryGetValue<string>(out var text) => text,
                null => "null",
                _ => pair.Value.ToJsonString(),
            };
            formatted = formatted.Replace("{" + pair.Key + "}", rendered);
        }

        var leftover = TemplateVariablePattern().Matches(formatted)
            .Select(match => match.Groups[1].Value)
            .Where(name => !name.StartsWith('<'))
            .ToList();
        if (leftover.Count > 0)
        {
            throw new ItsCompilationException(
                $"Missing required configuration for instruction type '{typeName}': {string.Join(", ", leftover)}");
        }
        return formatted;
    }

    private string GeneratePrompt(
        JsonArray content, Dictionary<string, JsonObject> instructionTypes, JsonObject template, JsonObject variables,
        Dictionary<string, JsonNode?> objectReferences)
    {
        var compilerConfig = template["compilerConfig"] as JsonObject ?? new JsonObject();
        var systemPrompt = compilerConfig["systemPrompt"]?.GetValue<string>() ?? _options.SystemPrompt;
        var instructionWrapper = compilerConfig["instructionWrapper"]?.GetValue<string>() ?? _options.InstructionWrapper;
        var baseInstructions = compilerConfig["processingInstructions"] is JsonArray custom
            ? custom.Select(item => item?.GetValue<string>() ?? "").ToList()
            : _options.ProcessingInstructions.ToList();

        // Reference data: variables named by placeholder dataSource configs are
        // rendered once above the template as context the model must not output
        var dataSources = ReferenceData.CollectDataSources(content);
        foreach (var name in objectReferences.Keys)
        {
            if (dataSources.All(request => request.Name != name))
            {
                dataSources.Add(new ReferenceData.DataSourceRequest(name, null));
            }
        }
        var referenceParts = new List<string>();
        if (dataSources.Count > 0)
        {
            referenceParts.Add("REFERENCE DATA");
            referenceParts.Add("");
            foreach (var request in dataSources)
            {
                if (!variables.TryGetPropertyValue(request.Name, out var value)
                    && !objectReferences.TryGetValue(request.Name, out value))
                {
                    throw new ItsCompilationException(
                        $"Unknown data source '{request.Name}': no variable with that name");
                }
                referenceParts.Add($"### {request.Name}");
                referenceParts.Add("");
                referenceParts.Add(ReferenceData.RenderDataSource(value, request.Limit));
                referenceParts.Add("");
            }
            baseInstructions.Add(ReferenceData.Instruction);
        }

        var processedContent = new StringBuilder();
        foreach (var element in content)
        {
            if (element is not JsonObject obj) continue;
            var type = obj["type"]?.GetValue<string>();
            if (type == "text")
            {
                processedContent.Append(obj["text"]?.GetValue<string>() ?? "");
            }
            else if (type == "placeholder")
            {
                var instruction = FormatInstruction(obj, instructionTypes);
                processedContent.Append(
                    instruction.StartsWith("<<", StringComparison.Ordinal) && instruction.EndsWith(">>", StringComparison.Ordinal)
                        ? instruction
                        : instructionWrapper.Replace("{instruction}", instruction));
            }
        }

        var parts = new List<string> { "INTRODUCTION", "", systemPrompt, "", "INSTRUCTIONS", "" };
        for (var i = 0; i < baseInstructions.Count; i++)
        {
            parts.Add($"{i + 1}. {baseInstructions[i]}");
        }
        parts.Add("");
        parts.AddRange(referenceParts);
        parts.Add("TEMPLATE");
        parts.Add("");
        parts.Add(processedContent.ToString());

        return string.Join("\n", parts);
    }
}
