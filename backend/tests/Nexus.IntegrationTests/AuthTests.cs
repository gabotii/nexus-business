using System.Net;
using System.Text.Json;
using Npgsql;

namespace Nexus.IntegrationTests;

/// <summary>Accounts and sessions (ADR-0002, R3, R4) against the real API and database.</summary>
[Collection(SharedApi.Name)]
public sealed class AuthTests(ApiFactory factory)
{
    [Fact]
    public async Task Register_verify_sign_in_and_sign_out()
    {
        using var session = new ApiSession(factory);
        var email = Accounts.NewEmail();

        await session.RegisterAsync(email, "  Lucía Gómez ");
        var verification = await factory.LatestEmailToAsync(email);
        Assert.Contains("Confirma tu correo", verification.Subject, StringComparison.Ordinal);
        Assert.StartsWith($"{ApiFactory.PublicBaseUrl}/verificar-correo?", verification.Link, StringComparison.Ordinal);

        await session.VerifyLatestAsync(factory, email);
        var login = await session.LoginAsync(email);
        await Problem.EnsureSuccessAsync(login);
        var me = await Json.ReadAsync(login);
        Assert.Equal("Lucía Gómez", me.GetProperty("displayName").GetString());
        Assert.True(me.GetProperty("emailVerified").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/api/v1/account/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await session.PostAsync("/api/v1/auth/logout")).StatusCode);
        await Problem.AssertAsync(await session.GetAsync("/api/v1/account/me"), HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task Session_cookie_is_http_only_secure_and_strict()
    {
        using var session = new ApiSession(factory);
        var email = Accounts.NewEmail();
        await session.RegisterAsync(email);

        var login = await session.LoginAsync(email);

        var setCookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("nexus.session=", StringComparison.Ordinal));
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        // Not "remember me": a browser-session cookie (the server-side session still expires after 8 h idle).
        Assert.DoesNotContain("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Antiforgery_works_over_plain_http_for_local_runs()
    {
        // Behind the TLS proxy requests are HTTPS (X-Forwarded-Proto); locally they are not, and must still work.
        using var http = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
        });

        var response = await http.GetAsync(new Uri("/api/v1/auth/antiforgery", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var xsrf = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        Assert.DoesNotContain("secure", xsrf, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", xsrf, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Antiforgery_cookies_are_secure_over_https()
    {
        using var session = new ApiSession(factory);

        var response = await session.GetAsync("/api/v1/auth/antiforgery");

        Assert.All(response.Headers.GetValues("Set-Cookie"),
            cookie => Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Registering_an_existing_address_answers_the_same_and_tells_the_owner()
    {
        using var session = new ApiSession(factory);
        var email = Accounts.NewEmail();
        await session.RegisterAsync(email);
        await session.VerifyLatestAsync(factory, email);

        var again = await session.PostAsync("/api/v1/auth/register",
            new { email = email.ToUpperInvariant(), password = "another-password-1", displayName = "Intruso" });

        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        Assert.Contains("Ya tienes una cuenta", (await factory.LatestEmailToAsync(email)).Subject, StringComparison.Ordinal);
        await Problem.AssertAsync(await session.LoginAsync(email, "another-password-1"), HttpStatusCode.Unauthorized,
            "invalid_credentials");
    }

    [Fact]
    public async Task Unknown_accounts_and_wrong_passwords_get_the_same_answer()
    {
        using var session = new ApiSession(factory);
        var email = Accounts.NewEmail();
        await session.RegisterAsync(email);

        var wrongPassword = await session.LoginAsync(email, "not-the-password");
        var unknownAccount = await session.LoginAsync(Accounts.NewEmail());

        await Problem.AssertAsync(wrongPassword, HttpStatusCode.Unauthorized, "invalid_credentials");
        await Problem.AssertAsync(unknownAccount, HttpStatusCode.Unauthorized, "invalid_credentials");
        Assert.Equal(Detail(await Json.ReadAsync(wrongPassword)), Detail(await Json.ReadAsync(unknownAccount)));
    }

    [Fact]
    public async Task Five_failed_sign_ins_lock_the_account()
    {
        using var session = new ApiSession(factory);
        var email = Accounts.NewEmail();
        await session.RegisterAsync(email);

        for (var i = 0; i < 5; i++)
        {
            await session.LoginAsync(email, "wrong-password-123");
        }

        await Problem.AssertAsync(await session.LoginAsync(email), HttpStatusCode.Unauthorized, "invalid_credentials");
        // The fifth failure locks the account; the sixth attempt (right password) is refused as locked out too.
        Assert.Equal(2L, await CountAuditAsync("auth.login_locked_out", email));
    }

    [Fact]
    public async Task Weak_passwords_are_rejected_before_any_account_lookup()
    {
        using var session = new ApiSession(factory);

        var response = await session.PostAsync("/api/v1/auth/register",
            new { email = Accounts.NewEmail(), password = "short", displayName = "Ana" });

        await Problem.AssertAsync(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True((await Json.ReadAsync(response)).GetProperty("errors").TryGetProperty("password", out _));
    }

    [Fact]
    public async Task State_changing_requests_need_the_antiforgery_token()
    {
        using var session = new ApiSession(factory);

        var response = await session.SendAsync(HttpMethod.Post, "/api/v1/auth/login",
            new { email = Accounts.NewEmail(), password = Accounts.Password }, withXsrf: false);

        await Problem.AssertAsync(response, HttpStatusCode.BadRequest, "csrf_failed");
    }

    [Fact]
    public async Task Signing_in_starts_a_new_session_and_revokes_the_presented_one()
    {
        using var session = await factory.SignedInUserAsync();
        var first = session.SessionCookie!;

        await Problem.EnsureSuccessAsync(await session.LoginAsync(session.Email));
        var second = session.SessionCookie!;

        Assert.NotEqual(first, second);
        using var replay = new ApiSession(factory);
        replay.UseSessionCookie(first);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/api/v1/account/me")).StatusCode);
        replay.UseSessionCookie(second);
        Assert.Equal(HttpStatusCode.OK, (await replay.GetAsync("/api/v1/account/me")).StatusCode);
    }

    [Fact]
    public async Task Signing_out_revokes_the_session_on_the_server()
    {
        using var session = await factory.SignedInUserAsync();
        var copied = session.SessionCookie!;

        await session.PostAsync("/api/v1/auth/logout");

        using var replay = new ApiSession(factory);
        replay.UseSessionCookie(copied);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/api/v1/account/me")).StatusCode);
    }

    [Fact]
    public async Task A_user_lists_their_devices_and_signs_one_out()
    {
        using var laptop = await factory.SignedInUserAsync();
        using var phone = new ApiSession(factory);
        await Problem.EnsureSuccessAsync(await phone.LoginAsync(laptop.Email));

        var sessions = await laptop.GetJsonAsync<JsonElement>("/api/v1/account/sessions");
        Assert.Equal(2, sessions.GetArrayLength());
        var other = sessions.EnumerateArray().Single(s => !s.GetProperty("isCurrent").GetBoolean());
        Assert.Matches(@"^\d+\.\d+\.\d+\.0/24$|/48$", other.GetProperty("ipPrefix").GetString() ?? "0.0.0.0/24");

        var revoke = await laptop.DeleteAsync($"/api/v1/account/sessions/{other.GetProperty("id").GetGuid()}");

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/v1/account/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await laptop.GetAsync("/api/v1/account/me")).StatusCode);
    }

    [Fact]
    public async Task Sessions_of_other_users_cannot_be_revoked()
    {
        using var alice = await factory.SignedInUserAsync();
        using var bob = await factory.SignedInUserAsync();
        var bobSession = (await bob.GetJsonAsync<JsonElement>("/api/v1/account/sessions"))[0].GetProperty("id").GetGuid();

        await Problem.AssertAsync(await alice.DeleteAsync($"/api/v1/account/sessions/{bobSession}"),
            HttpStatusCode.NotFound, "not_found");
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync("/api/v1/account/me")).StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_signs_out_every_other_device()
    {
        using var laptop = await factory.SignedInUserAsync();
        using var phone = new ApiSession(factory);
        await Problem.EnsureSuccessAsync(await phone.LoginAsync(laptop.Email));

        var change = await laptop.PostAsync("/api/v1/account/password",
            new { currentPassword = Accounts.Password, newPassword = "a-brand-new-passphrase" });

        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await laptop.GetAsync("/api/v1/account/me")).StatusCode);
        // A state-changing request still works on this device (the antiforgery token is still bound to the user).
        Assert.Equal(HttpStatusCode.OK,
            (await laptop.PutAsync("/api/v1/account/profile", new { displayName = "Ana P." })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/v1/account/me")).StatusCode);
        await Problem.AssertAsync(await phone.LoginAsync(laptop.Email), HttpStatusCode.Unauthorized, "invalid_credentials");
        await Problem.EnsureSuccessAsync(await phone.LoginAsync(laptop.Email, "a-brand-new-passphrase"));
    }

    [Fact]
    public async Task A_wrong_current_password_is_a_field_error()
    {
        using var session = await factory.SignedInUserAsync();

        var response = await session.PostAsync("/api/v1/account/password",
            new { currentPassword = "not-my-password", newPassword = "a-brand-new-passphrase" });

        await Problem.AssertAsync(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True((await Json.ReadAsync(response)).GetProperty("errors").TryGetProperty("currentPassword", out _));
    }

    [Fact]
    public async Task Password_reset_by_e_mail_sets_the_password_and_signs_out_everywhere()
    {
        using var session = await factory.SignedInUserAsync();
        using var anonymous = new ApiSession(factory);

        Assert.Equal(HttpStatusCode.Accepted,
            (await anonymous.PostAsync("/api/v1/auth/forgot-password", new { email = session.Email })).StatusCode);
        var mail = await factory.LatestEmailToAsync(session.Email);
        Assert.StartsWith($"{ApiFactory.PublicBaseUrl}/restablecer-contrasena?", mail.Link, StringComparison.Ordinal);

        var reset = await anonymous.PostAsync("/api/v1/auth/reset-password",
            new { userId = mail.Query("uid"), token = mail.Query("token"), newPassword = "reset-passphrase-42" });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/v1/account/me")).StatusCode);
        await Problem.EnsureSuccessAsync(await anonymous.LoginAsync(session.Email, "reset-passphrase-42"));

        // The link works once: the password change rotated the security stamp the token was bound to.
        var replay = await anonymous.PostAsync("/api/v1/auth/reset-password",
            new { userId = mail.Query("uid"), token = mail.Query("token"), newPassword = "third-passphrase-77" });
        await Problem.AssertAsync(replay, HttpStatusCode.UnprocessableEntity, "invalid_link");
    }

    [Fact]
    public async Task Password_recovery_for_an_unknown_address_answers_the_same_and_sends_nothing()
    {
        using var anonymous = new ApiSession(factory);
        var email = Accounts.NewEmail();

        var response = await anonymous.PostAsync("/api/v1/auth/forgot-password", new { email });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(await factory.EmailsToAsync(email));
    }

    [Fact]
    public async Task A_tampered_verification_link_is_rejected()
    {
        using var session = new ApiSession(factory);
        var email = Accounts.NewEmail();
        await session.RegisterAsync(email);
        var mail = await factory.LatestEmailToAsync(email);

        var response = await session.PostAsync("/api/v1/auth/verify-email",
            new { userId = mail.Query("uid"), token = mail.Query("token")[..^4] + "AAAA" });

        await Problem.AssertAsync(response, HttpStatusCode.UnprocessableEntity, "invalid_link");
    }

    [Fact]
    public async Task Session_rows_store_a_hash_of_the_key_and_an_encrypted_ticket()
    {
        using var session = await factory.SignedInUserAsync(name: "Marta Ruiz");

        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT s.key_hash, s.ticket FROM user_sessions s JOIN users u ON u.id = s.user_id WHERE u.email = @email",
            connection);
        command.Parameters.AddWithValue("email", session.Email);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        Assert.Equal(32, ((byte[])reader[0]).Length);
        var ticket = System.Text.Encoding.UTF8.GetString((byte[])reader[1]);
        Assert.DoesNotContain("Marta", ticket, StringComparison.Ordinal);
        Assert.DoesNotContain(session.Email, ticket, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sign_in_events_are_audited_without_a_tenant()
    {
        using var session = await factory.SignedInUserAsync();

        Assert.Equal(1L, await CountAuditAsync("auth.login_succeeded", session.Email));
    }

    private static string? Detail(JsonElement problem) => problem.GetProperty("detail").GetString();

    private async Task<long> CountAuditAsync(string action, string email)
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        // audit_log has FORCE ROW LEVEL SECURITY: even the owner reads account events only as their user.
        await using var command = new NpgsqlCommand("""
            SELECT set_config('app.user_id', (SELECT id::text FROM users WHERE email = @email), false);
            SELECT count(*) FROM audit_log
            WHERE action = @action AND tenant_id IS NULL AND actor_user_id = (SELECT id FROM users WHERE email = @email);
            """, connection);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("action", action);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.NextResultAsync();
        await reader.ReadAsync();
        return reader.GetInt64(0);
    }
}
