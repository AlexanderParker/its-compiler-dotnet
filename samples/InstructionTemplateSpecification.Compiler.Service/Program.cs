using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using InstructionTemplateSpecification;

// HTTP compile service exposing the standard ITS compile contract, matching
// the Python FastAPI service:
//   POST /compile  {"template": {...}, "variables": {...}}
//   GET  /health
// CORS origins come from ITS_CORS_ORIGINS (comma-separated); the default
// covers the published demo plus local dev origins. Processing limits are
// configurable through the ITS_* environment variables.
//
// A deployed instance is a public endpoint that costs its operator compute and
// egress. CORS does not protect it: browsers enforce it and anything else
// ignores it. Requests are therefore throttled per client address, and the
// body is capped before it is read. Both are configurable, and setting the
// limits to zero turns throttling off for a private deployment.

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

var defaultOrigins =
    "https://alexanderparker.github.io,"
    + "http://localhost:5173,http://127.0.0.1:5173,http://localhost:4173,http://127.0.0.1:4173";
var origins = (Environment.GetEnvironmentVariable("ITS_CORS_ORIGINS") ?? defaultOrigins)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(origins).AllowAnyHeader().WithMethods("GET", "POST", "OPTIONS")));

var perMinute = ReadLimit("ITS_RATE_LIMIT_PER_MINUTE", 30);
var maxRequestBytes = ReadLimit("ITS_MAX_REQUEST_BYTES", 512 * 1024);

// Reject an oversized body at the server before any handler reads it.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxRequestBytes);

// Held outside the options callback so the headers below can ask it how much
// quota a caller has left. The framework's limiter reports that only when it
// refuses, which is too late to be useful: a client that can see its remaining
// quota can slow down before it is refused at all.
PartitionedRateLimiter<HttpContext>? limiter = null;

if (perMinute > 0)
{
    limiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        // The health endpoint must answer even when a caller is throttled,
        // or the platform's own probe trips the limiter.
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            return RateLimitPartition.GetNoLimiter("health");
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            ClientAddress(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = perMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    });

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.OnRejected = async (context, token) =>
        {
            var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var value)
                ? (int)value.TotalSeconds
                : 60;
            var headers = context.HttpContext.Response.Headers;
            headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
            headers["RateLimit-Limit"] = perMinute.ToString(CultureInfo.InvariantCulture);
            headers["RateLimit-Remaining"] = "0";
            headers["RateLimit-Reset"] = retryAfter.ToString(CultureInfo.InvariantCulture);
            await context.HttpContext.Response.WriteAsJsonAsync(
                new
                {
                    ok = false,
                    error = "This demo service limits how often it can be called. "
                        + $"Try again in {retryAfter} seconds, or run it locally without limits.",
                },
                token);
        };

        options.GlobalLimiter = limiter;
    });
}

var app = builder.Build();
app.UseCors();
if (limiter is not null)
{
    // Registered before the limiter so the callback is in place while the
    // response is still being formed. Headers cannot be added once the body has
    // started, and by the time the pipeline unwinds it has.
    app.Use(async (context, next) =>
    {
        context.Response.OnStarting(() =>
        {
            // The health endpoint has no limiter partition, and a refused
            // request has already reported its own figures.
            if (!context.Request.Path.StartsWithSegments("/health")
                && context.Response.StatusCode != StatusCodes.Status429TooManyRequests)
            {
                var statistics = limiter.GetStatistics(context);
                if (statistics is not null)
                {
                    context.Response.Headers["RateLimit-Limit"] =
                        perMinute.ToString(CultureInfo.InvariantCulture);
                    context.Response.Headers["RateLimit-Remaining"] =
                        statistics.CurrentAvailablePermits.ToString(CultureInfo.InvariantCulture);
                }
            }

            return Task.CompletedTask;
        });

        await next();
    });

    app.UseRateLimiter();
}

app.MapGet("/health", () => Results.Json(new { status = "ok" }));

app.MapPost("/compile", async (HttpRequest request) =>
{
    JsonObject body;
    try
    {
        var text = await new StreamReader(request.Body).ReadToEndAsync();
        body = JsonNode.Parse(text) as JsonObject
            ?? throw new System.Text.Json.JsonException("Request body must be a JSON object");
    }
    catch (System.Text.Json.JsonException error)
    {
        return Results.BadRequest(CompileResponse(ok: false, prompt: null, error: $"Invalid JSON body: {error.Message}"));
    }

    if (body["template"] is not JsonObject template)
    {
        return Results.BadRequest(CompileResponse(ok: false, prompt: null, error: "Missing template object"));
    }

    try
    {
        var compiler = new ItsCompiler(CompilerOptions.FromEnvironment());
        var result = await compiler.CompileAsync(template, body["variables"] as JsonObject);
        return Results.Text(CompileResponse(ok: true, prompt: result.Prompt, error: null).ToJsonString(), "application/json");
    }
    catch (ItsCompilationException error)
    {
        return Results.Text(CompileResponse(ok: false, prompt: null, error: error.Message).ToJsonString(), "application/json");
    }
});

app.Run();

static JsonObject CompileResponse(bool ok, string? prompt, string? error) => new()
{
    ["ok"] = ok,
    ["prompt"] = prompt,
    ["warnings"] = new JsonArray(),
    ["error"] = error,
    ["compiler"] = "its-compiler (dotnet)",
};


static int ReadLimit(string name, int fallback)
{
    var raw = Environment.GetEnvironmentVariable(name);
    return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
        ? value
        : fallback;
}

/// <summary>
/// The caller's address, read from X-Forwarded-For.
/// </summary>
/// <remarks>
/// Each proxy appends the address it received the request from, so the real
/// client is the rightmost entry. Reading the leftmost would trust a header the
/// caller sets themselves, and for a rate limiter that is not subtle: rotating
/// a fake value would lift the limit entirely.
/// </remarks>
static string ClientAddress(HttpContext context)
{
    var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
    if (!string.IsNullOrWhiteSpace(forwarded))
    {
        var chain = forwarded.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (chain.Length > 0)
        {
            return chain[^1];
        }
    }

    // An unidentifiable caller shares one partition rather than bypassing the
    // limit, which is the safe direction to fail.
    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
