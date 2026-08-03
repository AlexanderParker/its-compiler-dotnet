using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace InstructionTemplateSpecification.Compiler.Service.Tests;

/// <summary>
/// The throttling in front of the compile endpoint.
/// </summary>
/// <remarks>
/// A deployed instance of this sample is a public endpoint costing its operator
/// compute and egress, and CORS does not protect it: browsers enforce CORS and
/// anything else ignores it. The limiter is the only control, so it is worth
/// holding to a test rather than to a reading of the code.
/// </remarks>
public sealed class RateLimitTests : IDisposable
{
    private const int Limit = 4;

    private readonly WebApplicationFactory<Program> factory;

    public RateLimitTests()
    {
        // Read while the host is built, so it must be set before the first
        // client is created rather than in a fixture that runs later.
        Environment.SetEnvironmentVariable("ITS_RATE_LIMIT_PER_MINUTE", Limit.ToString(CultureInfo.InvariantCulture));
        factory = new WebApplicationFactory<Program>();
    }

    public void Dispose()
    {
        factory.Dispose();
        Environment.SetEnvironmentVariable("ITS_RATE_LIMIT_PER_MINUTE", null);
    }

    private static readonly object Template = new
    {
        template = new
        {
            version = "1.0.0",
            content = new[] { new { type = "text", text = "hello" } },
        },
        variables = new { },
    };

    private static HttpRequestMessage Compile(string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/compile")
        {
            Content = JsonContent.Create(Template),
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return request;
    }

    private static int? HeaderValue(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
        && int.TryParse(values.FirstOrDefault(), out var parsed)
            ? parsed
            : null;

    [Fact]
    public async Task SuccessfulResponsesReportRemainingQuota()
    {
        // A client that can see what it has left can slow down before it is
        // refused. The framework's limiter reports quota only when it refuses,
        // which is too late to be useful.
        var client = factory.CreateClient();

        for (var call = 1; call <= Limit; call++)
        {
            var response = await client.SendAsync(Compile("198.51.100.7"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(Limit, HeaderValue(response, "RateLimit-Limit"));
            Assert.Equal(Limit - call, HeaderValue(response, "RateLimit-Remaining"));
        }
    }

    [Fact]
    public async Task ExceedingTheLimitIsRefusedWithSomethingToActOn()
    {
        var client = factory.CreateClient();

        for (var call = 0; call < Limit; call++)
        {
            await client.SendAsync(Compile("198.51.100.8"));
        }

        var refused = await client.SendAsync(Compile("198.51.100.8"));

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(0, HeaderValue(refused, "RateLimit-Remaining"));
        Assert.NotNull(refused.Headers.RetryAfter);

        // The message tells a visitor what to do rather than restating the
        // status code.
        var body = await refused.Content.ReadAsStringAsync();
        Assert.Contains("locally", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheRightmostForwardedAddressIsWhatCounts()
    {
        // THE ONE THAT MATTERS. Every entry but the last is set by the caller,
        // so partitioning on the leftmost would let anyone lift their own limit
        // by rotating a fake value. The limit must still bite when only the
        // caller-controlled part of the chain changes.
        var client = factory.CreateClient();

        for (var call = 0; call < Limit; call++)
        {
            var response = await client.SendAsync(Compile($"10.0.0.{call}, 203.0.113.9"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var refused = await client.SendAsync(Compile("10.0.0.99, 203.0.113.9"));

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
    }

    [Fact]
    public async Task DifferentCallersDoNotShareAnAllowance()
    {
        // The counterpart to the test above: partitioning must still separate
        // genuinely different callers, or one busy client throttles everyone.
        var client = factory.CreateClient();

        for (var call = 0; call < Limit; call++)
        {
            await client.SendAsync(Compile("203.0.113.20"));
        }

        var other = await client.SendAsync(Compile("203.0.113.21"));

        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task TheHealthEndpointIsNotThrottledOrAnnotated()
    {
        // A platform probe must not trip the limiter, and has no allowance to
        // report because it has no partition.
        var client = factory.CreateClient();

        for (var call = 0; call < Limit * 3; call++)
        {
            var response = await client.GetAsync("/health");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("RateLimit-Limit"));
            Assert.False(response.Headers.Contains("RateLimit-Remaining"));
        }
    }
}
