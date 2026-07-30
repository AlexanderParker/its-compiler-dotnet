using System.Net;
using System.Net.Sockets;
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
                if (response.Content.Headers.ContentLength is long length && length > _options.MaxSchemaResponseSize)
                {
                    throw new ItsSecurityException($"Schema too large: {length} bytes");
                }
                text = await response.Content.ReadAsStringAsync(timeout.Token);
            }
        }
        catch (Exception error) when (error is not ItsCompilationException)
        {
            throw new ItsSchemaException($"Failed to load schema from {resolved}: {error.Message}", error);
        }

        if (text.Length > _options.MaxSchemaResponseSize)
        {
            throw new ItsSecurityException($"Schema too large: {text.Length} characters");
        }
        if (JsonNode.Parse(text) is not JsonObject schema)
        {
            throw new ItsSchemaException($"Schema at {resolved} is not a JSON object");
        }
        ValidateSchemaStructure(schema, resolved);
        _cache[resolved] = schema;
        return schema;
    }

    private static void ValidateSchemaStructure(JsonObject schema, string url)
    {
        if (schema["instructionTypes"] is null) return;
        if (schema["instructionTypes"] is not JsonObject types)
        {
            throw new ItsSchemaException($"instructionTypes must be an object in schema {url}");
        }
        foreach (var pair in types)
        {
            if (pair.Value is not JsonObject definition
                || definition["template"] is not JsonValue value
                || !value.TryGetValue<string>(out _))
            {
                throw new ItsSchemaException($"Instruction type '{pair.Key}' in schema {url} is missing its template string");
            }
        }
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
        if (uri.AbsolutePath.Contains("..", StringComparison.Ordinal))
        {
            throw new ItsSecurityException($"Path traversal detected in schema URL: {url}");
        }

        // Trusted prefixes short-circuit domain and SSRF checks
        if (_options.TrustedSchemaPrefixes.Concat(_options.AllowedSchemaPrefixes)
            .Any(prefix => url.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return;
        }
        if (_options.RestrictSchemasToAllowlist)
        {
            throw new ItsSecurityException($"Schema URL not in allowlist: {url}");
        }

        var host = uri.Host.ToLowerInvariant();
        if (_options.BlockLocalhost && host is "localhost" or "127.0.0.1" or "0.0.0.0" or "::1")
        {
            throw new ItsSecurityException($"Localhost schema access blocked: {url}");
        }
        if (_options.BlockPrivateNetworks && IsBlockedAddress(host))
        {
            throw new ItsSecurityException($"Private network schema access blocked: {url}");
        }
        if (_options.EnforceDomainAllowlist)
        {
            var allowed = _options.DomainAllowlist.Any(domain =>
                host == domain || host.EndsWith("." + domain, StringComparison.Ordinal));
            if (!allowed)
            {
                throw new ItsSecurityException($"Schema domain '{host}' not in allowlist: {url}");
            }
        }
    }

    private static bool IsBlockedAddress(string host)
    {
        if (!IPAddress.TryParse(host, out var address))
        {
            return false;
        }
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254)
                || bytes[0] >= 224;
        }
        return address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal || address.Equals(IPAddress.IPv6Loopback);
    }
}
