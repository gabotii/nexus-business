using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Nexus.Domain.Tenancy;

namespace Nexus.IntegrationTests;

/// <summary>Company settings and logo upload (Phase 3, R12).</summary>
[Collection(SharedApi.Name)]
public sealed class SettingsTests(ApiFactory factory)
{
    internal static readonly byte[] Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01,
        0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0xF8, 0xCF, 0xC0, 0xF0, 0x1F, 0x00, 0x05, 0x00, 0x01, 0xFF, 0x89, 0x99, 0x3D, 0x1D, 0x00, 0x00,
        0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    [Fact]
    public async Task Owners_update_the_company_settings_with_optimistic_concurrency()
    {
        using var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync("Taller Ruiz");
        var t = $"/api/v1/tenants/{tenantId}";
        var settings = await owner.GetJsonAsync<JsonElement>($"{t}/settings");
        var version = settings.GetProperty("version").GetUInt32();

        var updated = await owner.PutAsync($"{t}/settings", new
        {
            name = "Taller Ruiz e Hijos", legalName = "Taller Ruiz S.L.", taxId = "B87654321", email = "hola@ruiz.example",
            phone = "910 000 000", address = new { line = "Av. Libertad 3", city = "Sevilla", countryCode = "es" },
            currency = "USD", timeZone = "America/Mexico_City", customerDuplicateRule = "taxId", version,
        });
        await Problem.EnsureSuccessAsync(updated);

        var workspace = await owner.GetJsonAsync<JsonElement>(t);
        Assert.Equal("Taller Ruiz e Hijos", workspace.GetProperty("name").GetString());
        Assert.Equal("USD", workspace.GetProperty("currency").GetString());
        Assert.StartsWith("taller-ruiz", workspace.GetProperty("slug").GetString(), StringComparison.Ordinal);
        var stored = await owner.GetJsonAsync<JsonElement>($"{t}/settings");
        Assert.Equal("taxId", stored.GetProperty("customerDuplicateRule").GetString());
        Assert.Equal("ES", stored.GetProperty("address").GetProperty("countryCode").GetString());

        var stale = await owner.PutAsync($"{t}/settings", new
        {
            name = "Otro", currency = "EUR", timeZone = "Europe/Madrid", customerDuplicateRule = "none", version,
        });
        await Problem.AssertAsync(stale, HttpStatusCode.Conflict, "concurrency_conflict");
    }

    [Fact]
    public async Task Invalid_settings_are_rejected()
    {
        using var owner = await factory.SignedInUserAsync();
        var t = $"/api/v1/tenants/{await owner.CreateCompanyAsync()}";
        var version = (await owner.GetJsonAsync<JsonElement>($"{t}/settings")).GetProperty("version").GetUInt32();

        var response = await owner.PutAsync($"{t}/settings", new
        {
            name = "Ok", currency = "XYZ", timeZone = "Mars/Olympus", customerDuplicateRule = "email", version,
        });

        await Problem.AssertAsync(response, HttpStatusCode.BadRequest, "validation_failed");
        var errors = (await Json.ReadAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("currency", out _));
        Assert.True(errors.TryGetProperty("timeZone", out _));
    }

    [Fact]
    public async Task Only_settings_managers_see_or_change_the_settings()
    {
        using var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync();
        using var manager = await factory.AddMemberAsync(owner, tenantId, SystemRoles.Manager);
        var t = $"/api/v1/tenants/{tenantId}";

        await Problem.AssertAsync(await manager.GetAsync($"{t}/settings"), HttpStatusCode.Forbidden, "permission_required");
        await Problem.AssertAsync(await UploadAsync(manager, t, Png, "logo.png"), HttpStatusCode.Forbidden, "permission_required");
    }

    [Fact]
    public async Task A_logo_is_uploaded_served_to_members_replaced_and_removed()
    {
        using var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync();
        using var employee = await factory.AddMemberAsync(owner, tenantId, SystemRoles.Employee);
        var t = $"/api/v1/tenants/{tenantId}";

        var upload = await UploadAsync(owner, t, Png, "evil.svg", "image/svg+xml");
        await Problem.EnsureSuccessAsync(upload);
        var firstLogo = (await Json.ReadAsync(upload)).GetProperty("logoFileId").GetGuid();
        Assert.Equal(firstLogo, (await employee.GetJsonAsync<JsonElement>(t)).GetProperty("logoFileId").GetGuid());

        // Served with the type detected from the bytes, not the one the client claimed.
        var served = await employee.GetAsync($"{t}/settings/logo");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/png", served.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await served.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", served.Headers.GetValues("X-Content-Type-Options").Single());

        var second = await UploadAsync(owner, t, Png, "logo.png");
        var secondLogo = (await Json.ReadAsync(second)).GetProperty("logoFileId").GetGuid();
        Assert.NotEqual(firstLogo, secondLogo);
        Assert.Equal("1", await Sql.AsTenantAsync(factory, tenantId, "SELECT count(*) FROM stored_files"));
        Assert.Single(Directory.GetFiles(Path.Combine(factory.StoragePath, tenantId.ToString("N"))));

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{t}/settings/logo")).StatusCode);
        await Problem.AssertAsync(await employee.GetAsync($"{t}/settings/logo"), HttpStatusCode.NotFound, "not_found");
        Assert.Empty(Directory.GetFiles(Path.Combine(factory.StoragePath, tenantId.ToString("N"))));
    }

    [Theory]
    [InlineData("svg")]
    [InlineData("html")]
    [InlineData("empty")]
    [InlineData("too-large")]
    public async Task Anything_but_a_small_png_jpeg_or_webp_is_refused(string kind)
    {
        using var owner = await factory.SignedInUserAsync();
        var t = $"/api/v1/tenants/{await owner.CreateCompanyAsync()}";
        var (bytes, code) = kind switch
        {
            "svg" => ("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"u8.ToArray(), "logo_format"),
            "html" => ("<html><body>hi</body></html>"u8.ToArray(), "logo_format"),
            "empty" => (Array.Empty<byte>(), "logo_size"),
            _ => ([.. Png, .. new byte[600 * 1024]], "logo_size"),
        };

        var response = await UploadAsync(owner, t, bytes, "logo.png", "image/png");

        Assert.True(response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest
            or HttpStatusCode.RequestEntityTooLarge, $"{response.StatusCode}");
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            Assert.Equal(code, await Problem.CodeOfAsync(response));
        }
    }

    internal static Task<HttpResponseMessage> UploadAsync(ApiSession session, string t, byte[] bytes, string fileName,
        string contentType = "image/png")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        return session.SendAsync(HttpMethod.Put, $"{t}/settings/logo", form);
    }
}
