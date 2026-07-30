using System.Text.Json.Nodes;

namespace Its.Compiler;

/// <summary>
/// Loads instruction type schemas referenced through extends. Https URLs are
/// allowed (optionally restricted to trusted prefixes); relative references
/// resolve against the template's base URL and, when they land on file URLs,
/// require <see cref="CompilerOptions.AllowLocalFileSchemas"/>.
/// </summary>
internal sealed class SchemaLoader
{
    private static readonly HttpClient SharedClient = new();

    private readonly CompilerOptions _options;
    private readonly Dictionary<string, JsonObject> _cache = new();

    public SchemaLoader(CompilerOptions options)
    {
        _options = options;
    }

    public async Task<JsonObject> LoadAsync(string schemaUrl, string? baseUrl, CancellationToken cancellationToken)
    {
        var resolved = Resolve(schemaUrl, baseUrl);
        ValidateUrl(resolved);

        if (_cache.TryGetValue(resolved, out var cached))
        {
            return cached;
        }

        string text;
        try
        {
            var uri = new Uri(resolved);
            if (uri.IsFile)
            {
                text = await File.ReadAllTextAsync(uri.LocalPath, cancellationToken);
            }
            else
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.ParseAdd("application/json");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_options.RequestTimeout);
                using var response = await SharedClient.SendAsync(request, timeout.Token);
                response.EnsureSuccessStatusCode();
                text = await response.Content.ReadAsStringAsync(timeout.Token);
            }
        }
        catch (Exception error) when (error is not ItsCompilationException)
        {
            throw new ItsSchemaException($"Failed to load schema from {resolved}: {error.Message}", error);
        }

        if (JsonNode.Parse(text) is not JsonObject schema)
        {
            throw new ItsSchemaException($"Schema at {resolved} is not a JSON object");
        }
        _cache[resolved] = schema;
        return schema;
    }

    private static string Resolve(string schemaUrl, string? baseUrl)
    {
        if (Uri.TryCreate(schemaUrl, UriKind.Absolute, out _))
        {
            return schemaUrl;
        }
        if (baseUrl is null)
        {
            throw new ItsSchemaException($"Cannot resolve relative schema reference '{schemaUrl}' without a base URL");
        }
        return new Uri(new Uri(baseUrl), schemaUrl).ToString();
    }

    private void ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ItsSecurityException($"Invalid schema URL: {url}");
        }
        if (uri.IsFile)
        {
            if (!_options.AllowLocalFileSchemas)
            {
                throw new ItsSecurityException($"Local file schemas are disabled: {url}");
            }
            return;
        }
        if (uri.Scheme == Uri.UriSchemeHttp && !_options.AllowHttp)
        {
            throw new ItsSecurityException($"HTTP schema URLs are not allowed: {url}");
        }
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ItsSecurityException($"Schema URL protocol not allowed: {uri.Scheme}");
        }
        if (_options.RestrictSchemasToAllowlist)
        {
            var allowed = _options.TrustedSchemaPrefixes.Concat(_options.AllowedSchemaPrefixes);
            if (!allowed.Any(prefix => url.StartsWith(prefix, StringComparison.Ordinal)))
            {
                throw new ItsSecurityException($"Schema URL not in allowlist: {url}");
            }
        }
    }
}
