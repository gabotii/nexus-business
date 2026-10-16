using System.Net;
using System.Text.Json;
using Nexus.Domain.Tenancy;

namespace Nexus.IntegrationTests;

/// <summary>Customers, tags and notes (Phase 3): search, duplicates, concurrency, archive and permissions.</summary>
[Collection(SharedApi.Name)]
public sealed class CustomerTests(ApiFactory factory)
{
    [Fact]
    public async Task A_customer_is_created_read_and_updated_with_its_version()
    {
        var (owner, t) = await NewCompanyAsync();

        var created = await owner.PostJsonAsync<JsonElement>($"{t}/customers", new
        {
            kind = "company",
            name = "  Panadería Núñez  ",
            legalName = "Panadería Núñez S.L.",
            taxId = "B-12.345.678",
            email = "Compras@Nunez.example",
            phone = "+34 600 000 000",
            address = new { line = "Calle Mayor 1", city = "Madrid", postalCode = "28013", region = "Madrid", countryCode = "es" },
        });
        Assert.Equal("Panadería Núñez", created.GetProperty("name").GetString());
        Assert.Equal("ES", created.GetProperty("address").GetProperty("countryCode").GetString());
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetUInt32();

        var updated = await owner.PutAsync($"{t}/customers/{id}", new
        {
            kind = "company", name = "Panadería Núñez e Hijos", taxId = "B12345678", email = "compras@nunez.example", version,
        });
        await Problem.EnsureSuccessAsync(updated);
        Assert.NotEqual(version, (await Json.ReadAsync(updated)).GetProperty("version").GetUInt32());

        // A second save with the stale version is refused instead of silently overwriting.
        var stale = await owner.PutAsync($"{t}/customers/{id}", new { kind = "company", name = "Otro nombre", version });
        await Problem.AssertAsync(stale, HttpStatusCode.Conflict, "concurrency_conflict");
        var current = await owner.GetJsonAsync<JsonElement>($"{t}/customers/{id}");
        Assert.Equal("Panadería Núñez e Hijos", current.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Search_ignores_case_and_accents_and_pages_the_results()
    {
        var (owner, t) = await NewCompanyAsync();
        foreach (var name in new[] { "Café Ñandú", "Cafetería Sol", "Ferretería López", "Zapatería Ruiz" })
        {
            await Problem.EnsureSuccessAsync(await owner.PostAsync($"{t}/customers", new { kind = "company", name }));
        }

        var byAccent = await owner.GetJsonAsync<JsonElement>($"{t}/customers?q=NANDU");
        Assert.Equal(["Café Ñandú"], Names(byAccent));

        var cafes = await owner.GetJsonAsync<JsonElement>($"{t}/customers?q=caf&pageSize=1&page=2");
        Assert.Equal(2, cafes.GetProperty("total").GetInt32());
        Assert.Equal(["Cafetería Sol"], Names(cafes));

        // Wildcards in the search term are literal characters, not patterns.
        Assert.Equal(0, (await owner.GetJsonAsync<JsonElement>($"{t}/customers?q=%25")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await owner.GetJsonAsync<JsonElement>($"{t}/customers?q=_")).GetProperty("total").GetInt32());

        await Problem.AssertAsync(await owner.GetAsync($"{t}/customers?pageSize=500"), HttpStatusCode.BadRequest,
            "validation_failed");
    }

    [Theory]
    [InlineData("EmailOrTaxId", "email", true)]
    [InlineData("EmailOrTaxId", "taxId", true)]
    [InlineData("Email", "taxId", false)]
    [InlineData("TaxId", "email", false)]
    [InlineData("None", "email", false)]
    public async Task The_company_duplicate_rule_decides_what_counts_as_a_duplicate(string rule, string field, bool refused)
    {
        var (owner, t) = await NewCompanyAsync();
        await SetDuplicateRuleAsync(owner, t, rule);
        var first = await owner.PostJsonAsync<JsonElement>($"{t}/customers",
            new { kind = "person", name = "Ana Pérez", email = "ana@example.test", taxId = "12345678Z" });

        var second = await owner.PostAsync($"{t}/customers", field == "email"
            ? (object)new { kind = "person", name = "Ana P.", email = " ANA@example.test ", taxId = (string?)null }
            : new { kind = "person", name = "Ana P.", email = (string?)null, taxId = "12.345.678-z" });

        if (refused)
        {
            await Problem.AssertAsync(second, HttpStatusCode.Conflict, "customer_duplicate");
            var problem = await Json.ReadAsync(second);
            Assert.Equal(first.GetProperty("id").GetGuid(), problem.GetProperty("existingCustomerId").GetGuid());
            Assert.Equal(field, problem.GetProperty("field").GetString());
        }
        else
        {
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        }
    }

    [Fact]
    public async Task Concurrent_creation_of_the_same_customer_lets_only_one_through()
    {
        var (owner, t) = await NewCompanyAsync();
        using var second = new ApiSession(factory);
        await Problem.EnsureSuccessAsync(await second.LoginAsync(owner.Email));

        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            (i % 2 == 0 ? owner : second).PostAsync($"{t}/customers",
                new { kind = "person", name = $"Luis {i}", email = "luis@example.test" })));

        Assert.Equal(1, attempts.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(5, attempts.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, (await owner.GetJsonAsync<JsonElement>($"{t}/customers?q=luis")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Archived_customers_leave_the_list_and_come_back_on_restore()
    {
        var (owner, t) = await NewCompanyAsync();
        var id = (await owner.PostJsonAsync<JsonElement>($"{t}/customers", new { kind = "person", name = "Eva Gil" }))
            .GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"{t}/customers/{id}/archive")).StatusCode);
        Assert.Equal(0, (await owner.GetJsonAsync<JsonElement>($"{t}/customers")).GetProperty("total").GetInt32());
        Assert.Equal(["Eva Gil"], Names(await owner.GetJsonAsync<JsonElement>($"{t}/customers?archived=true")));
        await Problem.AssertAsync(await owner.PostAsync($"{t}/customers/{id}/archive"), HttpStatusCode.UnprocessableEntity,
            "customer_archived");

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"{t}/customers/{id}/restore")).StatusCode);
        Assert.Equal(1, (await owner.GetJsonAsync<JsonElement>($"{t}/customers")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Restoring_is_refused_when_an_active_duplicate_exists()
    {
        var (owner, t) = await NewCompanyAsync();
        var old = (await owner.PostJsonAsync<JsonElement>($"{t}/customers",
            new { kind = "person", name = "Eva Gil", email = "eva@example.test" })).GetProperty("id").GetGuid();
        await owner.PostAsync($"{t}/customers/{old}/archive");
        // Archived customers still count as duplicates for new ones…
        await Problem.AssertAsync(await owner.PostAsync($"{t}/customers",
            new { kind = "person", name = "Eva", email = "eva@example.test" }), HttpStatusCode.Conflict, "customer_duplicate");

        await SetDuplicateRuleAsync(owner, t, "None");
        await owner.PostAsync($"{t}/customers", new { kind = "person", name = "Eva", email = "eva@example.test" });
        await SetDuplicateRuleAsync(owner, t, "Email");

        // …and an archived one cannot come back while an active one has the same address.
        await Problem.AssertAsync(await owner.PostAsync($"{t}/customers/{old}/restore"), HttpStatusCode.Conflict,
            "customer_duplicate");
    }

    [Fact]
    public async Task Tags_are_assigned_filtered_renamed_and_removed()
    {
        var (owner, t) = await NewCompanyAsync();
        var vip = (await owner.PostJsonAsync<JsonElement>($"{t}/tags", new { name = "VIP", color = "amber" })).GetProperty("id").GetGuid();
        var wholesale = (await owner.PostJsonAsync<JsonElement>($"{t}/tags", new { name = "Mayorista", color = "teal" }))
            .GetProperty("id").GetGuid();
        await Problem.AssertAsync(await owner.PostAsync($"{t}/tags", new { name = "vip", color = "red" }), HttpStatusCode.Conflict,
            "tag_name_taken");
        await Problem.AssertAsync(await owner.PostAsync($"{t}/tags", new { name = "Raro", color = "#ff00ff" }),
            HttpStatusCode.BadRequest, "validation_failed");

        await owner.PostAsync($"{t}/customers", new { kind = "person", name = "Ana", tagIds = new[] { vip, wholesale } });
        await owner.PostAsync($"{t}/customers", new { kind = "person", name = "Bea", tagIds = new[] { wholesale } });

        Assert.Equal(["Ana"], Names(await owner.GetJsonAsync<JsonElement>($"{t}/customers?tagId={vip}")));
        var tags = await owner.GetJsonAsync<JsonElement>($"{t}/tags");
        Assert.Equal(2, tags.EnumerateArray().Single(x => x.GetProperty("name").GetString() == "Mayorista")
            .GetProperty("customerCount").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{t}/tags/{vip}")).StatusCode);
        var ana = (await owner.GetJsonAsync<JsonElement>($"{t}/customers?q=ana")).GetProperty("items")[0];
        Assert.Equal(["Mayorista"], ana.GetProperty("tags").EnumerateArray().Select(x => x.GetProperty("name").GetString()!));
    }

    [Fact]
    public async Task Notes_are_listed_newest_first_and_only_their_author_deletes_them()
    {
        var (owner, t) = await NewCompanyAsync();
        using var colleague = await factory.AddMemberAsync(owner, Guid.Parse(t.Split('/')[^1]), SystemRoles.Employee);
        var id = (await owner.PostJsonAsync<JsonElement>($"{t}/customers", new { kind = "person", name = "Raúl" }))
            .GetProperty("id").GetGuid();

        var first = await owner.PostJsonAsync<JsonElement>($"{t}/customers/{id}/notes", new { body = "Prefiere factura en papel." });
        await colleague.PostJsonAsync<JsonElement>($"{t}/customers/{id}/notes", new { body = "Llamar los lunes." });

        var notes = await owner.GetJsonAsync<JsonElement>($"{t}/customers/{id}/notes");
        Assert.Equal(["Llamar los lunes.", "Prefiere factura en papel."],
            notes.EnumerateArray().Select(n => n.GetProperty("body").GetString()));
        Assert.Equal("Carlos Díaz", notes[0].GetProperty("authorName").GetString());
        Assert.False(notes[0].GetProperty("isYours").GetBoolean());

        var noteId = first.GetProperty("id").GetGuid();
        await Problem.AssertAsync(await colleague.DeleteAsync($"{t}/customers/{id}/notes/{noteId}"), HttpStatusCode.Forbidden,
            "note_not_yours");
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{t}/customers/{id}/notes/{noteId}")).StatusCode);

        // The note text never reaches the audit log.
        var audit = await Sql.AsTenantAsync(factory, Guid.Parse(t.Split('/')[^1]),
            "SELECT string_agg(coalesce(metadata::text, ''), ' ') FROM audit_log WHERE action LIKE 'customers.note%'");
        Assert.DoesNotContain("papel", audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_only_members_see_customers_but_cannot_change_them()
    {
        var (owner, t) = await NewCompanyAsync();
        var tenantId = Guid.Parse(t.Split('/')[^1]);
        using var reader = await factory.AddMemberAsync(owner, tenantId, SystemRoles.ReadOnly);
        var id = (await owner.PostJsonAsync<JsonElement>($"{t}/customers", new { kind = "person", name = "Ana" }))
            .GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"{t}/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"{t}/customers/{id}/notes")).StatusCode);
        foreach (var response in new[]
                 {
                     await reader.PostAsync($"{t}/customers", new { kind = "person", name = "Nuevo" }),
                     await reader.PostAsync($"{t}/customers/{id}/archive"),
                     await reader.PostAsync($"{t}/customers/{id}/notes", new { body = "Hola" }),
                     await reader.PostAsync($"{t}/tags", new { name = "X", color = "red" }),
                 })
        {
            await Problem.AssertAsync(response, HttpStatusCode.Forbidden, "permission_required");
        }
    }

    [Fact]
    public async Task Invalid_customer_data_is_reported_per_field()
    {
        var (owner, t) = await NewCompanyAsync();

        var response = await owner.PostAsync($"{t}/customers", new
        {
            kind = "person", name = "A", email = "not-an-email", phone = "call me", address = new { countryCode = "Spain" },
        });

        await Problem.AssertAsync(response, HttpStatusCode.BadRequest, "validation_failed");
        var errors = (await Json.ReadAsync(response)).GetProperty("errors");
        foreach (var field in new[] { "name", "email", "phone", "address.countryCode" })
        {
            Assert.True(errors.TryGetProperty(field, out _), $"missing error for {field}: {errors}");
        }
    }

    private async Task<(ApiSession Owner, string Base)> NewCompanyAsync()
    {
        var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync();
        return (owner, $"/api/v1/tenants/{tenantId}");
    }

    internal static async Task SetDuplicateRuleAsync(ApiSession owner, string t, string rule)
    {
        var settings = await owner.GetJsonAsync<JsonElement>($"{t}/settings");
        await Problem.EnsureSuccessAsync(await owner.PutAsync($"{t}/settings", new
        {
            name = settings.GetProperty("name").GetString(),
            currency = settings.GetProperty("currency").GetString(),
            timeZone = settings.GetProperty("timeZone").GetString(),
            customerDuplicateRule = JsonNamingCamel(rule),
            version = settings.GetProperty("version").GetUInt32(),
        }));
    }

    private static string JsonNamingCamel(string value) => JsonNamingPolicy.CamelCase.ConvertName(value);

    private static string[] Names(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("name").GetString()!)];
}
