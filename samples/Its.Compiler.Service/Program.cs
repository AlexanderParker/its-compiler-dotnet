using System.Text.Json.Nodes;
using Its.Compiler;

// HTTP compile service exposing the standard ITS compile contract, matching
// the Python FastAPI service:
//   POST /compile  {"template": {...}, "variables": {...}}
//   GET  /health
// CORS origins come from ITS_CORS_ORIGINS (comma-separated); the default
// covers the published demo plus local dev origins. Processing limits are
// configurable through the ITS_* environment variables.

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

var app = builder.Build();
app.UseCors();

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
