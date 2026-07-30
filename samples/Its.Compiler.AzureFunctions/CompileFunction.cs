using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Its.Compiler.AzureFunctions;

/// <summary>
/// HTTP compile endpoint matching the ITS compile service contract:
/// POST /api/compile {"template": {...}, "variables": {...}} returns
/// {"ok", "prompt", "warnings", "error", "compiler"}. Deploy behind API
/// Management for authentication, rate limiting and subscription keys.
/// Processing limits are configurable through ITS_* application settings.
/// </summary>
public class CompileFunction
{
    [Function("compile")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "compile")] HttpRequestData request)
    {
        var response = request.CreateResponse();
        JsonObject body;
        try
        {
            var text = await new StreamReader(request.Body).ReadToEndAsync();
            body = JsonNode.Parse(text) as JsonObject
                ?? throw new JsonException("Request body must be a JSON object");
        }
        catch (JsonException error)
        {
            response.StatusCode = HttpStatusCode.BadRequest;
            await WriteResultAsync(response, ok: false, prompt: null, error: $"Invalid JSON body: {error.Message}");
            return response;
        }

        if (body["template"] is not JsonObject template)
        {
            response.StatusCode = HttpStatusCode.BadRequest;
            await WriteResultAsync(response, ok: false, prompt: null, error: "Missing template object");
            return response;
        }

        try
        {
            var compiler = new ItsCompiler(CompilerOptions.FromEnvironment());
            var result = await compiler.CompileAsync(template, body["variables"] as JsonObject);
            response.StatusCode = HttpStatusCode.OK;
            await WriteResultAsync(response, ok: true, prompt: result.Prompt, error: null);
        }
        catch (ItsCompilationException error)
        {
            response.StatusCode = HttpStatusCode.OK;
            await WriteResultAsync(response, ok: false, prompt: null, error: error.Message);
        }
        return response;
    }

    private static async Task WriteResultAsync(HttpResponseData response, bool ok, string? prompt, string? error)
    {
        response.Headers.Add("Content-Type", "application/json");
        var payload = new JsonObject
        {
            ["ok"] = ok,
            ["prompt"] = prompt,
            ["warnings"] = new JsonArray(),
            ["error"] = error,
            ["compiler"] = "its-compiler (dotnet)",
        };
        await response.WriteStringAsync(payload.ToJsonString());
    }
}
