namespace Its.Compiler;

/// <summary>
/// Compiler configuration: security policy, processing limits and prompt
/// defaults. Limits are operator-configurable per the specification's
/// Processing Limits section; a template can never raise the limits of the
/// compiler processing it.
/// </summary>
public sealed class CompilerOptions
{
    // Network security
    public bool AllowHttp { get; set; }

    /// <summary>Permit extends to resolve local file paths relative to the template. Off by default.</summary>
    public bool AllowLocalFileSchemas { get; set; }

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

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Maximum schema response size in bytes.</summary>
    public int MaxSchemaResponseSize { get; set; } = 10 * 1024 * 1024;

    /// <summary>Maximum number of custom instruction types per template.</summary>
    public int MaxCustomTypes { get; set; } = 50;

    /// <summary>Scan text elements, string variables and config values for dangerous content patterns. On by default.</summary>
    public bool EnableContentScanning { get; set; } = true;

    // Processing limits (defaults match the reference compilers)
    public int MaxTemplateSize { get; set; } = 1024 * 1024;
    public int MaxContentElements { get; set; } = 1000;
    public int MaxNestingDepth { get; set; } = 10;
    public int MaxVariableCount { get; set; } = 10000;
    public int MaxVariableArrayItems { get; set; } = 1000;
    public int MaxTextLength { get; set; } = 10000;
    public int MaxExpressionLength { get; set; } = 500;
    public int MaxExtends { get; set; } = 10;

    // Prompt defaults (identical wording to the reference compilers)
    public string SystemPrompt { get; set; } =
        "You are an AI assistant that fills in content templates. Follow the instructions exactly and replace each "
        + "placeholder with appropriate content based on the user prompts provided. Respond only with the transformed content.";

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
    public required string Prompt { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}
