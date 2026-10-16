using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Nexus.Api.Controllers;

namespace Nexus.IntegrationTests;

/// <summary>Foundation behaviour shared by every endpoint: health, error contract, headers, limits, configuration.</summary>
[Collection(SharedApi.Name)]
public sealed class PlatformTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Readiness_is_healthy_when_the_api_runs_as_the_runtime_role()
    {
        var response = await _client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Liveness_does_not_depend_on_the_database()
    {
        var response = await _client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Meta_returns_the_configured_product_name()
    {
        var meta = await _client.GetFromJsonAsync<MetaResponse>(new Uri("/api/v1/meta", UriKind.Relative));

        Assert.NotNull(meta);
        Assert.Equal(ApiFactory.ProductName, meta.ProductName);
        Assert.Equal("v1", meta.ApiVersion);
        Assert.Matches(@"^\d+\.\d+\.\d+", meta.Version);
    }

    [Fact]
    public async Task Api_responses_carry_security_headers_and_are_never_cached()
    {
        var response = await _client.GetAsync(new Uri("/api/v1/meta", UriKind.Relative));

        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", Header(response, "Content-Security-Policy"));
        Assert.False(response.Headers.Contains("Server"));
        Assert.Matches("^[0-9a-f]{32}$", Header(response, "X-Correlation-Id"));
    }

    [Fact]
    public async Task Unknown_routes_answer_a_problem_with_code_and_correlation_id()
    {
        var response = await _client.GetAsync(new Uri("/api/v1/does-not-exist", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ReadJson(response);
        Assert.Equal("not_found", problem.GetProperty("code").GetString());
        Assert.Equal(Header(response, "X-Correlation-Id"), problem.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task Unexpected_errors_never_leak_internal_details()
    {
        var response = await _client.GetAsync(new Uri("/api/v1/test-only/errors/unhandled", UriKind.Relative));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("hunter2", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", body, StringComparison.Ordinal);
        var problem = JsonDocument.Parse(body).RootElement;
        Assert.Equal("internal_error", problem.GetProperty("code").GetString());
        Assert.False(problem.TryGetProperty("exception", out _));
    }

    [Theory]
    [InlineData("domain", HttpStatusCode.UnprocessableEntity, "insufficient_stock")]
    [InlineData("not-found", HttpStatusCode.NotFound, "not_found")]
    [InlineData("forbidden", HttpStatusCode.Forbidden, "permission_required")]
    [InlineData("conflict", HttpStatusCode.Conflict, "concurrency_conflict")]
    [InlineData("validation", HttpStatusCode.BadRequest, "validation_failed")]
    public async Task Known_errors_map_to_stable_status_codes(string kind, HttpStatusCode status, string code)
    {
        var response = await _client.GetAsync(new Uri($"/api/v1/test-only/errors/{kind}", UriKind.Relative));

        Assert.Equal(status, response.StatusCode);
        var problem = await ReadJson(response);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task Validation_errors_are_keyed_by_the_json_path_the_client_sent()
    {
        var response = await _client.GetAsync(new Uri("/api/v1/test-only/errors/validation", UriKind.Relative));

        var problem = await ReadJson(response);
        var messages = problem.GetProperty("errors").GetProperty("lines[0].quantity");
        Assert.Equal("Debe ser positiva.", messages[0].GetString());
    }

    [Fact]
    public async Task Requests_over_the_per_ip_budget_are_rejected_with_retry_after()
    {
        using var limited = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:PerIpPerMinute"] = "3" })));
        using var client = limited.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/api/v1/meta", UriKind.Relative))).StatusCode);
        }

        var rejected = await client.GetAsync(new Uri("/api/v1/meta", UriKind.Relative));
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);
        Assert.Equal("rate_limited", (await ReadJson(rejected)).GetProperty("code").GetString());
    }

    [Fact]
    public void Production_refuses_to_migrate_on_startup()
    {
        using var production = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.ConfigureAppConfiguration((_, c) =>
                c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:MigrateOnStartup"] = "true" }));
        });

        var error = Record.Exception(() => production.CreateClient());

        Assert.NotNull(error);
        Assert.Contains("must be false in Production", Flatten(error), StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_fails_fast_without_a_runtime_connection_string()
    {
        using var misconfigured = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "",
                ["Database:MigrateOnStartup"] = "false",
            })));

        var error = Record.Exception(() => misconfigured.CreateClient());

        Assert.NotNull(error);
        Assert.Contains("ConnectionStrings:Default is required", Flatten(error), StringComparison.Ordinal);
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values)
        : response.Content.Headers.TryGetValues(name, out var contentValues) ? string.Join(",", contentValues)
        : string.Empty;

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static string Flatten(Exception error)
    {
        var messages = new List<string>();
        for (var e = error; e is not null; e = e.InnerException)
        {
            messages.Add(e.Message);
        }

        return string.Join(" | ", messages);
    }
}
