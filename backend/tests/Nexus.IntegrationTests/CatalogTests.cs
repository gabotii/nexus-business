using System.Net;
using System.Text.Json;
using Nexus.Domain.Tenancy;

namespace Nexus.IntegrationTests;

/// <summary>Product categories and products (Phase 3): SKUs, amounts, cost visibility and archiving.</summary>
[Collection(SharedApi.Name)]
public sealed class CatalogTests(ApiFactory factory)
{
    private static object Product(string sku, string name, decimal price = 2.5m, decimal? cost = 1.2m, Guid? categoryId = null,
        string kind = "product", bool trackInventory = true, uint? version = null) => new
    {
        kind, sku, name, description = (string?)null, categoryId, unit = "unit", unitPrice = price, cost, taxRate = 21m,
        trackInventory, minStock = 5m, version,
    };

    [Fact]
    public async Task Products_keep_exact_decimal_amounts()
    {
        var (owner, t, _) = await NewCompanyAsync();

        var created = await owner.PostJsonAsync<JsonElement>($"{t}/products", Product("PAN-001", "Pan de masa madre", 3.1416m, 0.0001m));

        Assert.Equal(3.1416m, created.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(0.0001m, created.GetProperty("cost").GetDecimal());
        Assert.Equal(21m, created.GetProperty("taxRate").GetDecimal());
        var listed = await owner.GetJsonAsync<JsonElement>($"{t}/products?q=masa");
        Assert.Equal(3.1416m, listed.GetProperty("items")[0].GetProperty("unitPrice").GetDecimal());
    }

    [Theory]
    [InlineData(1.23456, 21, 0)]
    [InlineData(-1, 21, 0)]
    [InlineData(1, 100.5, 0)]
    [InlineData(1, 21.123, 0)]
    [InlineData(1, 21, 0.0001)]
    public async Task Out_of_range_or_over_precise_amounts_are_rejected(double price, double taxRate, double minStock)
    {
        var (owner, t, _) = await NewCompanyAsync();

        var response = await owner.PostAsync($"{t}/products", new
        {
            kind = "product", sku = "X-1", name = "Producto", unit = "unit", unitPrice = (decimal)price, taxRate = (decimal)taxRate,
            trackInventory = true, minStock = (decimal)minStock,
        });

        await Problem.AssertAsync(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Skus_are_unique_per_company_regardless_of_case()
    {
        var (owner, t, _) = await NewCompanyAsync();
        await Problem.EnsureSuccessAsync(await owner.PostAsync($"{t}/products", Product("pan-001", "Pan")));

        await Problem.AssertAsync(await owner.PostAsync($"{t}/products", Product("PAN-001", "Otro pan")), HttpStatusCode.Conflict,
            "sku_taken");

        // Another company may use the same SKU.
        var (other, t2, _) = await NewCompanyAsync();
        Assert.Equal(HttpStatusCode.Created, (await other.PostAsync($"{t2}/products", Product("PAN-001", "Pan"))).StatusCode);
    }

    [Fact]
    public async Task Services_never_track_stock()
    {
        var (owner, t, _) = await NewCompanyAsync();

        await Problem.AssertAsync(await owner.PostAsync($"{t}/products", Product("SRV-1", "Instalación", kind: "service")),
            HttpStatusCode.BadRequest, "validation_failed");
        var service = await owner.PostJsonAsync<JsonElement>($"{t}/products",
            Product("SRV-1", "Instalación", kind: "service", trackInventory: false));
        Assert.Equal(0m, service.GetProperty("minStock").GetDecimal());
    }

    [Fact]
    public async Task Cost_is_hidden_from_roles_without_view_cost_and_survives_their_edits()
    {
        var (owner, t, tenantId) = await NewCompanyAsync();
        using var employee = await factory.AddMemberAsync(owner, tenantId, SystemRoles.Employee);
        // Employees may manage products here, but still not see costs.
        var employeeRole = await owner.RoleIdAsync(tenantId, SystemRoles.Employee);
        await Problem.EnsureSuccessAsync(await owner.PutAsync($"{t}/roles/{employeeRole}", new
        {
            name = "Empleado",
            permissions = SystemRoles.Get(SystemRoles.Employee).Permissions.Append(Permissions.ProductsManage),
        }));
        var product = await owner.PostJsonAsync<JsonElement>($"{t}/products", Product("CAF-1", "Café", 1.5m, 0.4m));
        var id = product.GetProperty("id").GetGuid();

        var seen = await employee.GetJsonAsync<JsonElement>($"{t}/products/{id}");
        Assert.Equal(JsonValueKind.Null, seen.GetProperty("cost").ValueKind);
        Assert.Equal(JsonValueKind.Null,
            (await employee.GetJsonAsync<JsonElement>($"{t}/products")).GetProperty("items")[0].GetProperty("cost").ValueKind);

        // The employee edits the price and sends a cost: the cost is ignored, not overwritten.
        await Problem.EnsureSuccessAsync(await employee.PutAsync($"{t}/products/{id}",
            Product("CAF-1", "Café", 1.6m, cost: 0m, version: seen.GetProperty("version").GetUInt32())));

        var afterwards = await owner.GetJsonAsync<JsonElement>($"{t}/products/{id}");
        Assert.Equal(1.6m, afterwards.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(0.4m, afterwards.GetProperty("cost").GetDecimal());
    }

    [Fact]
    public async Task Categories_group_products_and_archived_ones_cannot_receive_new_products()
    {
        var (owner, t, _) = await NewCompanyAsync();
        var bakery = (await owner.PostJsonAsync<JsonElement>($"{t}/product-categories", new { name = "Panadería" })).GetProperty("id").GetGuid();
        var drinks = (await owner.PostJsonAsync<JsonElement>($"{t}/product-categories", new { name = "Bebidas" })).GetProperty("id").GetGuid();
        await Problem.AssertAsync(await owner.PostAsync($"{t}/product-categories", new { name = "PANADERIA" }), HttpStatusCode.Conflict,
            "category_name_taken");
        var bread = await owner.PostJsonAsync<JsonElement>($"{t}/products", Product("PAN-1", "Pan", categoryId: bakery));
        await owner.PostAsync($"{t}/products", Product("AGU-1", "Agua", categoryId: drinks));

        var inBakery = await owner.GetJsonAsync<JsonElement>($"{t}/products?categoryId={bakery}");
        Assert.Equal(["Pan"], inBakery.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("name").GetString()));
        Assert.Equal("Panadería", inBakery.GetProperty("items")[0].GetProperty("category").GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"{t}/product-categories/{bakery}/archive")).StatusCode);
        await Problem.AssertAsync(await owner.PostAsync($"{t}/products", Product("PAN-2", "Baguette", categoryId: bakery)),
            HttpStatusCode.UnprocessableEntity, "category_archived");
        // A product already in the archived category can still be edited.
        await Problem.EnsureSuccessAsync(await owner.PutAsync($"{t}/products/{bread.GetProperty("id").GetGuid()}",
            Product("PAN-1", "Pan blanco", categoryId: bakery, version: bread.GetProperty("version").GetUInt32())));
    }

    [Fact]
    public async Task Archived_products_leave_the_catalog_list()
    {
        var (owner, t, _) = await NewCompanyAsync();
        var id = (await owner.PostJsonAsync<JsonElement>($"{t}/products", Product("OLD-1", "Antiguo"))).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"{t}/products/{id}/archive")).StatusCode);

        Assert.Equal(0, (await owner.GetJsonAsync<JsonElement>($"{t}/products")).GetProperty("total").GetInt32());
        Assert.Equal(1, (await owner.GetJsonAsync<JsonElement>($"{t}/products?archived=true")).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"{t}/products/{id}/restore")).StatusCode);
    }

    [Fact]
    public async Task Price_changes_are_audited_with_old_and_new_values()
    {
        var (owner, t, tenantId) = await NewCompanyAsync();
        var p = await owner.PostJsonAsync<JsonElement>($"{t}/products", Product("TE-1", "Té", 2m));
        await owner.PutAsync($"{t}/products/{p.GetProperty("id").GetGuid()}",
            Product("TE-1", "Té", 2.25m, version: p.GetProperty("version").GetUInt32()));

        var metadata = await Sql.AsTenantAsync(factory, tenantId,
            "SELECT metadata::text FROM audit_log WHERE action = 'products.updated'");

        using var doc = JsonDocument.Parse(metadata!);
        Assert.Equal(2m, doc.RootElement.GetProperty("price").GetProperty("from").GetDecimal());
        Assert.Equal(2.25m, doc.RootElement.GetProperty("price").GetProperty("to").GetDecimal());
    }

    [Fact]
    public async Task Read_only_members_see_the_catalog_but_cannot_change_it()
    {
        var (owner, t, tenantId) = await NewCompanyAsync();
        using var reader = await factory.AddMemberAsync(owner, tenantId, SystemRoles.ReadOnly);

        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"{t}/products")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"{t}/product-categories")).StatusCode);
        await Problem.AssertAsync(await reader.PostAsync($"{t}/products", Product("R-1", "No")), HttpStatusCode.Forbidden,
            "permission_required");
        await Problem.AssertAsync(await reader.PostAsync($"{t}/product-categories", new { name = "No" }), HttpStatusCode.Forbidden,
            "permission_required");
    }

    private async Task<(ApiSession Owner, string Base, Guid TenantId)> NewCompanyAsync()
    {
        var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync();
        return (owner, $"/api/v1/tenants/{tenantId}", tenantId);
    }
}
