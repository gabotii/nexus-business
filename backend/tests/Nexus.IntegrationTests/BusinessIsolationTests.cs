using System.Net;
using System.Text.Json;
using Npgsql;

namespace Nexus.IntegrationTests;

/// <summary>
/// ADR-0004 for the Phase 3 modules: a member of company A reaches nothing of company B through A's routes — not
/// by id in the path, not by id in the body — and the database refuses cross-company rows on its own.
/// </summary>
[Collection(SharedApi.Name)]
public sealed class BusinessIsolationTests(ApiFactory factory)
{
    private static readonly string[] NewTables =
        ["customers", "customer_tags", "customer_notes", "tags", "product_categories", "products", "stored_files"];

    [Fact]
    public async Task Another_companys_records_are_not_found_through_your_company()
    {
        var b = await SeedCompanyAsync("Empresa B");
        using var ownerA = await factory.SignedInUserAsync();
        var a = $"/api/v1/tenants/{await ownerA.CreateCompanyAsync("Empresa A")}";
        var customerOfA = (await ownerA.PostJsonAsync<JsonElement>($"{a}/customers", new { kind = "person", name = "Cliente A" }))
            .GetProperty("id").GetGuid();

        var attempts = new (string What, Task<HttpResponseMessage> Call)[]
        {
            ("read customer", ownerA.GetAsync($"{a}/customers/{b.CustomerId}")),
            ("update customer", ownerA.PutAsync($"{a}/customers/{b.CustomerId}", new { kind = "person", name = "Hack", version = 1 })),
            ("archive customer", ownerA.PostAsync($"{a}/customers/{b.CustomerId}/archive")),
            ("read notes", ownerA.GetAsync($"{a}/customers/{b.CustomerId}/notes")),
            ("add note", ownerA.PostAsync($"{a}/customers/{b.CustomerId}/notes", new { body = "hola" })),
            ("delete note", ownerA.DeleteAsync($"{a}/customers/{b.CustomerId}/notes/{b.NoteId}")),
            ("tag of B on A's customer", ownerA.PutAsync($"{a}/customers/{customerOfA}",
                new { kind = "person", name = "Cliente A", tagIds = new[] { b.TagId }, version = 0 })),
            ("new customer with B's tag", ownerA.PostAsync($"{a}/customers",
                new { kind = "person", name = "Nuevo", tagIds = new[] { b.TagId } })),
            ("rename tag", ownerA.PutAsync($"{a}/tags/{b.TagId}", new { name = "Hack", color = "red" })),
            ("delete tag", ownerA.DeleteAsync($"{a}/tags/{b.TagId}")),
            ("rename category", ownerA.PutAsync($"{a}/product-categories/{b.CategoryId}", new { name = "Hack" })),
            ("archive category", ownerA.PostAsync($"{a}/product-categories/{b.CategoryId}/archive")),
            ("read product", ownerA.GetAsync($"{a}/products/{b.ProductId}")),
            ("update product", ownerA.PutAsync($"{a}/products/{b.ProductId}", NewProduct("HACK", null, version: 1))),
            ("archive product", ownerA.PostAsync($"{a}/products/{b.ProductId}/archive")),
            ("product in B's category", ownerA.PostAsync($"{a}/products", NewProduct("A-1", b.CategoryId))),
        };

        foreach (var (what, call) in attempts)
        {
            var response = await call;
            Assert.True(response.StatusCode == HttpStatusCode.NotFound,
                $"{what}: expected 404, got {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        // Lists of A never contain B's data, whatever the filter.
        Assert.Equal(1, (await ownerA.GetJsonAsync<JsonElement>($"{a}/customers?q=cliente")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await ownerA.GetJsonAsync<JsonElement>($"{a}/customers?tagId={b.TagId}")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await ownerA.GetJsonAsync<JsonElement>($"{a}/products?categoryId={b.CategoryId}")).GetProperty("total").GetInt32());
        Assert.Empty((await ownerA.GetJsonAsync<JsonElement>($"{a}/tags")).EnumerateArray());
        await Problem.AssertAsync(await ownerA.GetAsync($"{a}/settings/logo"), HttpStatusCode.NotFound, "not_found");

        // And B is untouched.
        var bCustomer = await b.Owner.GetJsonAsync<JsonElement>($"{b.Base}/customers/{b.CustomerId}");
        Assert.Equal("Cliente B", bCustomer.GetProperty("name").GetString());
        Assert.False(bCustomer.GetProperty("archived").GetBoolean());
        Assert.Single((await b.Owner.GetJsonAsync<JsonElement>($"{b.Base}/customers/{b.CustomerId}/notes")).EnumerateArray());
    }

    [Fact]
    public async Task Outsiders_cannot_reach_a_companys_logo()
    {
        var b = await SeedCompanyAsync("Con logo");
        using var outsider = await factory.SignedInUserAsync();

        await TenancyKit.AssertTenantNotFoundAsync(await outsider.GetAsync($"{b.Base}/settings/logo"));
        using var anonymous = new ApiSession(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"{b.Base}/settings/logo")).StatusCode);
    }

    [Fact]
    public async Task Without_a_tenant_context_the_new_tables_show_no_rows()
    {
        var b = await SeedCompanyAsync("Datos B");

        foreach (var table in NewTables)
        {
            Assert.Equal("0", await Sql.AsTenantAsync(factory, null, $"SELECT count(*) FROM {table}"));
            Assert.NotEqual("0", await Sql.AsTenantAsync(factory, b.TenantId, $"SELECT count(*) FROM {table}"));
        }
    }

    [Fact]
    public async Task The_database_refuses_a_product_in_another_companys_category()
    {
        var a = await SeedCompanyAsync("Empresa A");
        var b = await SeedCompanyAsync("Empresa B");

        var error = await Assert.ThrowsAsync<PostgresException>(() => Sql.AsTenantAsync(factory, a.TenantId,
            $"UPDATE products SET category_id = '{b.CategoryId}' WHERE id = '{a.ProductId}'"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    private static object NewProduct(string sku, Guid? categoryId, uint? version = null) => new
    {
        kind = "product", sku, name = "Producto", categoryId, unit = "unit", unitPrice = 1m, taxRate = 21m,
        trackInventory = false, minStock = 0m, version,
    };

    private sealed record Seeded(ApiSession Owner, Guid TenantId, string Base, Guid CustomerId, Guid NoteId, Guid TagId,
        Guid CategoryId, Guid ProductId);

    /// <summary>A company with one record of every Phase 3 kind, logo included.</summary>
    private async Task<Seeded> SeedCompanyAsync(string name)
    {
        var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync(name);
        var t = $"/api/v1/tenants/{tenantId}";
        var tag = (await owner.PostJsonAsync<JsonElement>($"{t}/tags", new { name = "VIP", color = "amber" })).GetProperty("id").GetGuid();
        var customer = (await owner.PostJsonAsync<JsonElement>($"{t}/customers",
            new { kind = "person", name = "Cliente B", tagIds = new[] { tag } })).GetProperty("id").GetGuid();
        var note = (await owner.PostJsonAsync<JsonElement>($"{t}/customers/{customer}/notes", new { body = "Nota de B" }))
            .GetProperty("id").GetGuid();
        var category = (await owner.PostJsonAsync<JsonElement>($"{t}/product-categories", new { name = "Categoría" }))
            .GetProperty("id").GetGuid();
        var product = (await owner.PostJsonAsync<JsonElement>($"{t}/products", NewProduct("B-1", category))).GetProperty("id").GetGuid();
        await Problem.EnsureSuccessAsync(await SettingsTests.UploadAsync(owner, t, SettingsTests.Png, "logo.png"));
        return new Seeded(owner, tenantId, t, customer, note, tag, category, product);
    }
}
