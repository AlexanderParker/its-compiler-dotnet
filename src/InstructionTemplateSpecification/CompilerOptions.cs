namespace InstructionTemplateSpecification;

/// <summary>
/// Compiler configuration: security policy, processing limits and prompt
/// defaults. Limits are operator-configurable per the specification's
/// Processing Limits section; a template can never raise the limits of the
/// compiler processing it.
/// </summary>
public sealed class CompilerOptions
{
    // Network security

    /// <summary>Permit schema URLs over plain http. Off by default; https only.</summary>
    public bool AllowHttp { get; set; }

    /// <summary>Permit extends to resolve local file paths relative to the template. Off by default.</summary>
    public bool AllowLocalFileSchemas { get; set; }

    /// <summary>Schema URL prefixes trusted without further checks. Pre-populated with the published specification locations.</summary>
    public IList<string> TrustedSchemaPrefixes { get; } = new List<string>
    {
        "https://alexanderparker.github.io/instruction-template-specification/",
        "https://raw.githubusercontent.com/alexanderparker/instruction-template-specification/",
    };

    /// <summary>Additional allowed schema URL prefixes beyond the built-in trusted patterns.</summary>
    public IList<string> AllowedSchemaPrefixes { get; } = new List<string>();

    /// <summary>When false, any https schema URL is allowed; when true, only trusted or allowed prefixes.</summary>
    public bool RestrictSchemasToAllowlist { get; set; }

    /// <summary>Domains schema URLs may resolve from when <see cref="EnforceDomainAllowlist"/> is on. Subdomains match.</summary>
    public IList<string> DomainAllowlist { get; } = new List<string> { "alexanderparker.github.io" };

    /// <summary>Enforce <see cref="DomainAllowlist"/> for schema URLs outside the trusted prefixes. On by default, matching the Python compiler.</summary>
    public bool EnforceDomainAllowlist { get; set; } = true;

    /// <summary>Block schema URLs that name or resolve to localhost. On by default.</summary>
    public bool BlockLocalhost { get; set; } = true;

    /// <summary>Block schema URLs that name or resolve to private, loopback or link-local addresses. On by default.</summary>
    public bool BlockPrivateNetworks { get; set; } = true;

    /// <summary>How long a single schema fetch may take before it is abandoned.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Maximum schema response size in bytes.</summary>
    public int MaxSchemaResponseSize { get; set; } = 10 * 1024 * 1024;

    /// <summary>Maximum number of custom instruction types per template.</summary>
    public int MaxCustomTypes { get; set; } = 50;

    /// <summary>Scan text elements, string variables and config values for dangerous content patterns. On by default.</summary>
    public bool EnableContentScanning { get; set; } = true;

    // Processing limits (defaults match the reference compilers)

    /// <summary>Largest template accepted, in bytes.</summary>
    public int MaxTemplateSize { get; set; } = 1024 * 1024;

    /// <summary>Most content elements a template may contain, counted across all nesting levels.</summary>
    public int MaxContentElements { get; set; } = 1000;

    /// <summary>Deepest nesting of conditionals permitted.</summary>
    public int MaxNestingDepth { get; set; } = 10;

    /// <summary>Most variables accepted, counting nested properties.</summary>
    public int MaxVariableCount { get; set; } = 10000;

    /// <summary>Most items permitted in any one array variable.</summary>
    public int MaxVariableArrayItems { get; set; } = 1000;

    /// <summary>Longest string value accepted, in characters. Longer values are truncated when substituted.</summary>
    public int MaxTextLength { get; set; } = 10000;

    /// <summary>Longest conditional expression accepted, in characters.</summary>
    public int MaxExpressionLength { get; set; } = 500;

    /// <summary>Most type libraries a template may extend.</summary>
    public int MaxExtends { get; set; } = 10;

    // Prompt defaults (identical wording to the reference compilers)

    /// <summary>Opening instruction placed above the compiled template.</summary>
    public string SystemPrompt { get; set; } =
        "You are an AI assistant that fills in content templates. Follow the instructions exactly and replace each "
        + "placeholder with appropriate content based on the user prompts provided. Respond only with the transformed content.";

    /// <summary>Numbered rules telling the model how to treat placeholders and reference data.</summary>
    public IList<string> ProcessingInstructions { get; set; } = new List<string>
    {
        "Replace each placeholder marked with << >> with generated content",
        "The user's content request is wrapped in ([{< >}]) to distinguish it from instructions",
        "Follow the format requirements specified after each user prompt",
        "Maintain the existing structure and formatting of the template",
        "Only replace the placeholders - do not modify any other text",
        "Generate content that matches the tone and style requested",
        "Respond only with the transformed content - do not include any explanations or additional text",
    };

    /// <summary>Wrapper placed around each compiled placeholder instruction. {instruction} is substituted.</summary>
    public string InstructionWrapper { get; set; } = "<<{instruction}>>";

    /// <summary>
    /// Reads limit overrides from ITS_* environment variables, matching the
    /// Python compiler's names (ITS_MAX_VARIABLE_COUNT and friends).
    /// </summary>
    public static CompilerOptions FromEnvironment()
    {
        var options = new CompilerOptions();
        static int? ReadInt(string name) =>
            int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : null;

        options.MaxTemplateSize = ReadInt("ITS_MAX_TEMPLATE_SIZE") ?? options.MaxTemplateSize;
        options.MaxContentElements = ReadInt("ITS_MAX_CONTENT_ELEMENTS") ?? options.MaxContentElements;
        options.MaxNestingDepth = ReadInt("ITS_MAX_NESTING_DEPTH") ?? options.MaxNestingDepth;
        options.MaxVariableCount = ReadInt("ITS_MAX_VARIABLE_COUNT") ?? options.MaxVariableCount;
        options.MaxVariableArrayItems = ReadInt("ITS_MAX_VARIABLE_ARRAY_ITEMS") ?? options.MaxVariableArrayItems;
        options.MaxTextLength = ReadInt("ITS_MAX_TEXT_LENGTH") ?? options.MaxTextLength;
        if (Environment.GetEnvironmentVariable("ITS_ALLOW_HTTP") == "true") options.AllowHttp = true;
        if (Environment.GetEnvironmentVariable("ITS_ALLOW_LOCAL_SCHEMAS") == "true") options.AllowLocalFileSchemas = true;
        return options;
    }
}

/// <summary>The result of compiling a template.</summary>
public sealed class CompilationResult
{
    /// <summary>The compiled prompt, ready to send to a model.</summary>
    public required string Prompt { get; init; }

    /// <summary>Non-fatal issues found while compiling, such as an overridden instruction type.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
