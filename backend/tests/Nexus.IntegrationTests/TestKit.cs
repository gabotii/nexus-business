using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Npgsql;

namespace Nexus.IntegrationTests;

/// <summary>
/// A browser-like client: keeps cookies (the API only issues <c>Secure</c> cookies, so it talks HTTPS to the test
/// server) and echoes the <c>XSRF-TOKEN</c> cookie in <c>X-XSRF-TOKEN</c> on unsafe requests, refreshing it once
/// on <c>csrf_failed</c> exactly like the SPA's HTTP client.
/// </summary>
public sealed class ApiSession : IDisposable
{
    private static readonly Uri BaseAddress = new("https://localhost");

    public ApiSession(ApiFactory factory)
    {
        Cookies = new CookieContainer();
        Http = factory.CreateDefaultClient(BaseAddress, new CookieContainerHandler(Cookies));
    }

    public CookieContainer Cookies { get; }

    public HttpClient Http { get; }

    /// <summary>The account signed in on this session, when created by <see cref="Accounts.SignedInUserAsync"/>.</summary>
    public string Email { get; set; } = string.Empty;

    public string? SessionCookie => Cookies.GetCookies(BaseAddress)["nexus.session"]?.Value;

    /// <summary>Replaces the session cookie, to replay a cookie copied from another session.</summary>
    public void UseSessionCookie(string value) => Cookies.Add(BaseAddress, new Cookie("nexus.session", value) { Secure = true });

    public Task<HttpResponseMessage> GetAsync(string path) => Http.GetAsync(new Uri(path, UriKind.Relative));

    public Task<HttpResponseMessage> PostAsync(string path, object? body = null) => SendAsync(HttpMethod.Post, path, body);

    public Task<HttpResponseMessage> PutAsync(string path, object? body) => SendAsync(HttpMethod.Put, path, body);

    public Task<HttpResponseMessage> DeleteAsync(string path) => SendAsync(HttpMethod.Delete, path, null);

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, bool withXsrf = true)
    {
        if (withXsrf && Xsrf is null)
        {
            await RefreshXsrfAsync();
        }

        var response = await SendOnceAsync(method, path, body, withXsrf);
        if (withXsrf && await Problem.CodeOfAsync(response) == "csrf_failed")
        {
            await RefreshXsrfAsync();
            response = await SendOnceAsync(method, path, body, withXsrf);
        }

        return response;
    }

    public async Task<T> GetJsonAsync<T>(string path)
    {
        var response = await GetAsync(path);
        await Problem.EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<T>(Json.Options))!;
    }

    public async Task<T> PostJsonAsync<T>(string path, object? body = null)
    {
        var response = await PostAsync(path, body);
        await Problem.EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<T>(Json.Options))!;
    }

    public void Dispose() => Http.Dispose();

    private string? Xsrf => Cookies.GetCookies(BaseAddress)["XSRF-TOKEN"]?.Value;

    private async Task RefreshXsrfAsync() => (await GetAsync("/api/v1/auth/antiforgery")).EnsureSuccessStatusCode();

    private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string path, object? body, bool withXsrf)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = body as HttpContent ?? JsonContent.Create(body, options: Json.Options);
        }

        if (withXsrf && Xsrf is { } token)
        {
            request.Headers.Add("X-XSRF-TOKEN", token);
        }

        var response = await Http.SendAsync(request);
        // Buffer so the body can be inspected more than once (problem code checks, then assertions).
        await response.Content.LoadIntoBufferAsync();
        return response;
    }
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}

public static class Problem
{
    public static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentType?.MediaType != "application/problem+json")
        {
            return null;
        }

        var body = await Json.ReadAsync(response);
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>Asserts the status and the problem <c>code</c>.</summary>
    public static async Task AssertAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {(int)status} {code}, got {(int)response.StatusCode}: {body}");
        Assert.Equal(code, await CodeOfAsync(response));
    }

    public static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            Assert.Fail($"{(int)response.StatusCode} {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: " +
                        await response.Content.ReadAsStringAsync());
        }
    }
}

/// <summary>A delivered (queued) e-mail and the link it contains.</summary>
public sealed record SentEmail(string To, string Subject, string Body)
{
    public string Link => Regex.Match(Body, @"https://app\.test/\S+").Value;

    public string Query(string name) =>
        Uri.UnescapeDataString(Regex.Match(Link, $@"[?&]{name}=([^&\s]+)").Groups[1].Value);
}

public static class Accounts
{
    public const string Password = "correct-horse-battery";

    public static string NewEmail(string prefix = "user") => $"{prefix}-{Guid.NewGuid():N}@example.test";

    /// <summary>Most recent e-mails to <paramref name="to"/>, newest first (the outbox has no RLS).</summary>
    public static async Task<IReadOnlyList<SentEmail>> EmailsToAsync(this ApiFactory factory, string to)
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT recipient, subject, body_text FROM email_outbox WHERE recipient = @to ORDER BY created_at DESC, id DESC",
            connection);
        command.Parameters.AddWithValue("to", to);
        await using var reader = await command.ExecuteReaderAsync();
        var emails = new List<SentEmail>();
        while (await reader.ReadAsync())
        {
            emails.Add(new SentEmail(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? "" : reader.GetString(2)));
        }

        return emails;
    }

    public static async Task<SentEmail> LatestEmailToAsync(this ApiFactory factory, string to) =>
        (await factory.EmailsToAsync(to)) is [var latest, ..] ? latest : throw new InvalidOperationException($"No e-mail to {to}.");

    public static async Task RegisterAsync(this ApiSession session, string email, string name = "Ana Pérez",
        string password = Password) =>
        Assert.Equal(HttpStatusCode.Accepted,
            (await session.PostAsync("/api/v1/auth/register", new { email, password, displayName = name })).StatusCode);

    public static async Task VerifyLatestAsync(this ApiSession session, ApiFactory factory, string email)
    {
        var mail = await factory.LatestEmailToAsync(email);
        var response = await session.PostAsync("/api/v1/auth/verify-email",
            new { userId = mail.Query("uid"), token = mail.Query("token") });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    public static Task<HttpResponseMessage> LoginAsync(this ApiSession session, string email, string password = Password,
        bool rememberMe = false) =>
        session.PostAsync("/api/v1/auth/login", new { email, password, rememberMe });

    /// <summary>A new account with a verified address, signed in on a fresh session.</summary>
    public static async Task<ApiSession> SignedInUserAsync(this ApiFactory factory, string? email = null,
        string name = "Ana Pérez", bool verified = true)
    {
        email ??= NewEmail();
        var session = new ApiSession(factory);
        await session.RegisterAsync(email, name);
        if (verified)
        {
            await session.VerifyLatestAsync(factory, email);
        }

        await Problem.EnsureSuccessAsync(await session.LoginAsync(email));
        session.Email = email;
        return session;
    }
}

