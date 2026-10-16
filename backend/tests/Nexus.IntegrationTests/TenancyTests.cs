using System.Net;
using System.Text.Json;
using Nexus.Domain.Tenancy;

namespace Nexus.IntegrationTests;

/// <summary>Companies, membership resolution, permissions and anti-escalation rules (ADR-0003, ADR-0005, R2).</summary>
[Collection(SharedApi.Name)]
public sealed class TenancyTests(ApiFactory factory)
{
    [Fact]
    public async Task Creating_a_company_makes_the_creator_its_owner_with_five_system_roles()
    {
        using var owner = await factory.SignedInUserAsync();

        var tenantId = await owner.CreateCompanyAsync("Panadería Núñez & Hijos");

        var mine = await owner.GetJsonAsync<JsonElement>("/api/v1/tenants");
        var company = mine.EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == tenantId);
        Assert.Equal("Panadería Núñez & Hijos", company.GetProperty("name").GetString());
        Assert.StartsWith("panaderia-nunez-hijos", company.GetProperty("slug").GetString(), StringComparison.Ordinal);
        Assert.Equal(SystemRoles.Owner, company.GetProperty("role").GetProperty("systemKey").GetString());

        var workspace = await owner.GetJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}");
        Assert.Equal("EUR", workspace.GetProperty("currency").GetString());
        Assert.Equal("Europe/Madrid", workspace.GetProperty("timeZone").GetString());
        Assert.Equal(Permissions.All, workspace.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));

        var roles = await owner.GetJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/roles");
        Assert.Equal(SystemRoles.Defaults.Select(d => d.Key), roles.EnumerateArray().Select(r => r.GetProperty("systemKey").GetString()));
    }

    [Fact]
    public async Task Two_companies_with_the_same_name_get_distinct_slugs()
    {
        using var first = await factory.SignedInUserAsync();
        using var second = await factory.SignedInUserAsync();
        var name = $"Ferretería {Guid.NewGuid():N}"[..20];

        var a = await first.CreateCompanyAsync(name);
        var b = await second.CreateCompanyAsync(name);

        var slugA = (await first.GetJsonAsync<JsonElement>($"/api/v1/tenants/{a}")).GetProperty("slug").GetString();
        var slugB = (await second.GetJsonAsync<JsonElement>($"/api/v1/tenants/{b}")).GetProperty("slug").GetString();
        Assert.NotEqual(slugA, slugB);
        Assert.StartsWith(slugA!, slugB, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unverified_account_cannot_create_a_company()
    {
        using var session = await factory.SignedInUserAsync(verified: false);

        await Problem.AssertAsync(await session.PostAsync("/api/v1/tenants", new { name = "Mi empresa" }),
            HttpStatusCode.Forbidden, "email_not_verified");
    }

    [Fact]
    public async Task Company_data_requires_a_session()
    {
        using var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync();
        using var anonymous = new ApiSession(factory);

        await Problem.AssertAsync(await anonymous.GetAsync($"/api/v1/tenants/{tenantId}/members"),
            HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task Outsiders_get_the_same_404_for_existing_and_missing_companies()
    {
        using var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync();
        using var outsider = await factory.SignedInUserAsync();

        foreach (var path in new[] { "", "/members", "/roles", "/invitations" })
        {
            await TenancyKit.AssertTenantNotFoundAsync(await outsider.GetAsync($"/api/v1/tenants/{tenantId}{path}"));
            await TenancyKit.AssertTenantNotFoundAsync(await outsider.GetAsync($"/api/v1/tenants/{Guid.NewGuid()}{path}"));
        }

        // Writes too, whatever the body says.
        await TenancyKit.AssertTenantNotFoundAsync(await outsider.PostAsync($"/api/v1/tenants/{tenantId}/roles",
            new { name = "Intruso", permissions = Permissions.All, tenantId }));
        Assert.DoesNotContain((await outsider.GetJsonAsync<JsonElement>("/api/v1/tenants")).EnumerateArray(),
            t => t.GetProperty("id").GetGuid() == tenantId);
    }

    [Fact]
    public async Task Identifiers_of_another_company_are_not_found_through_your_own_company()
    {
        using var ownerA = await factory.SignedInUserAsync();
        var tenantA = await ownerA.CreateCompanyAsync("Empresa A");
        using var memberA = await factory.AddMemberAsync(ownerA, tenantA, SystemRoles.Employee);

        using var ownerB = await factory.SignedInUserAsync();
        var tenantB = await ownerB.CreateCompanyAsync("Empresa B");
        using var memberB = await factory.AddMemberAsync(ownerB, tenantB, SystemRoles.Employee);
        var memberOfB = await ownerB.MembershipIdAsync(tenantB, memberB.Email);
        var roleOfB = await ownerB.RoleIdAsync(tenantB, SystemRoles.Manager);
        var customRoleOfB = (await ownerB.PostJsonAsync<JsonElement>($"/api/v1/tenants/{tenantB}/roles",
            new { name = "Cajero", permissions = new[] { Permissions.SalesView } })).GetProperty("id").GetGuid();
        await ownerB.PostAsync($"/api/v1/tenants/{tenantB}/invitations",
            new { email = Accounts.NewEmail(), roleId = roleOfB });
        var invitationOfB = (await ownerB.GetJsonAsync<JsonElement>($"/api/v1/tenants/{tenantB}/invitations"))[0]
            .GetProperty("id").GetGuid();
        var memberOfA = await ownerA.MembershipIdAsync(tenantA, memberA.Email);
        var a = $"/api/v1/tenants/{tenantA}";

        // B's member, role and invitation through A's routes: all 404, as if they did not exist.
        await AssertNotFound(await ownerA.PutAsync($"{a}/members/{memberOfB}/role", new { roleId = roleOfB }));
        await AssertNotFound(await ownerA.PostAsync($"{a}/members/{memberOfB}/disable"));
        await AssertNotFound(await ownerA.PutAsync($"{a}/members/{memberOfA}/role", new { roleId = roleOfB }));
        await AssertNotFound(await ownerA.PutAsync($"{a}/roles/{customRoleOfB}",
            new { name = "Hackeado", permissions = Array.Empty<string>() }));
        await AssertNotFound(await ownerA.DeleteAsync($"{a}/roles/{customRoleOfB}"));
        await AssertNotFound(await ownerA.PostAsync($"{a}/invitations", new { email = Accounts.NewEmail(), roleId = roleOfB }));
        await AssertNotFound(await ownerA.PostAsync($"{a}/invitations/{invitationOfB}/resend"));
        await AssertNotFound(await ownerA.DeleteAsync($"{a}/invitations/{invitationOfB}"));

        // Nothing changed in B.
        var membersOfB = await ownerB.GetJsonAsync<JsonElement>($"/api/v1/tenants/{tenantB}/members");
        Assert.Equal(SystemRoles.Employee, membersOfB.EnumerateArray()
            .Single(m => m.GetProperty("id").GetGuid() == memberOfB).GetProperty("role").GetProperty("systemKey").GetString());
        Assert.Equal(1, (await ownerB.GetJsonAsync<JsonElement>($"/api/v1/tenants/{tenantB}/invitations")).GetArrayLength());
        Assert.Contains((await ownerB.GetJsonAsync<JsonElement>($"/api/v1/tenants/{tenantB}/roles")).EnumerateArray(),
            r => r.GetProperty("name").GetString() == "Cajero");

        static Task AssertNotFound(HttpResponseMessage response) =>
            Problem.AssertAsync(response, HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Each_role_sees_only_what_its_permissions_allow()
    {
        using var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync();
        using var employee = await factory.AddMemberAsync(owner, tenantId, SystemRoles.Employee);
        using var manager = await factory.AddMemberAsync(owner, tenantId, SystemRoles.Manager);
        var t = $"/api/v1/tenants/{tenantId}";

        // Everyone opens the workspace and gets their own permission list.
        var employeePermissions = (await employee.GetJsonAsync<JsonElement>(t)).GetProperty("permissions")
            .EnumerateArray().Select(p => p.GetString()!).ToHashSet();
        Assert.Equal(SystemRoles.Get(SystemRoles.Employee).Permissions.ToHashSet(), employeePermissions);

        await Problem.AssertAsync(await employee.GetAsync($"{t}/members"), HttpStatusCode.Forbidden, "permission_required");
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"{t}/members")).StatusCode);
        await Problem.AssertAsync(await manager.PostAsync($"{t}/invitations",
                new { email = Accounts.NewEmail(), roleId = await owner.RoleIdAsync(tenantId, SystemRoles.Employee) }),
            HttpStatusCode.Forbidden, "permission_required");
        await Problem.AssertAsync(await manager.PostAsync($"{t}/roles",
                new { name = "Nuevo", permissions = Array.Empty<string>() }),
            HttpStatusCode.Forbidden, "permission_required");
    }

    [Fact]
    public async Task Revoking_a_permission_takes_effect_on_the_next_request()
    {
        using var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync();
        using var manager = await factory.AddMemberAsync(owner, tenantId, SystemRoles.Manager);
        var t = $"/api/v1/tenants/{tenantId}";
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"{t}/members")).StatusCode);

        var managerRole = await owner.RoleIdAsync(tenantId, SystemRoles.Manager);
        await Problem.EnsureSuccessAsync(await owner.PutAsync($"{t}/roles/{managerRole}",
            new { name = "Responsable", permissions = new[] { Permissions.SalesView } }));

        await Problem.AssertAsync(await manager.GetAsync($"{t}/members"), HttpStatusCode.Forbidden, "permission_required");
    }

    [Fact]
    public async Task Nobody_changes_their_own_role_or_the_role_they_hold()
    {
        using var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync();
        using var admin = await factory.AddMemberAsync(owner, tenantId, SystemRoles.Administrator);
        var t = $"/api/v1/tenants/{tenantId}";
        var adminMembership = await owner.MembershipIdAsync(tenantId, admin.Email);
        var adminRole = await owner.RoleIdAsync(tenantId, SystemRoles.Administrator);

        await Problem.AssertAsync(await admin.PutAsync($"{t}/members/{adminMembership}/role",
            new { roleId = await owner.RoleIdAsync(tenantId, SystemRoles.Manager) }), HttpStatusCode.Forbidden, "cannot_modify_self");
        await Problem.AssertAsync(await admin.PostAsync($"{t}/members/{adminMembership}/disable"),
            HttpStatusCode.Forbidden, "cannot_modify_self");
        await Problem.AssertAsync(await admin.PutAsync($"{t}/roles/{adminRole}",
            new { name = "Administrador", permissions = Permissions.All }), HttpStatusCode.Forbidden, "cannot_modify_self");
    }

    [Fact]
    public async Task Administrators_cannot_grant_ownership_or_permissions_they_lack()
    {
        using var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync();
        using var admin = await factory.AddMemberAsync(owner, tenantId, SystemRoles.Administrator);
        using var employee = await factory.AddMemberAsync(owner, tenantId, SystemRoles.Employee);
        var t = $"/api/v1/tenants/{tenantId}";
        var employeeMembership = await owner.MembershipIdAsync(tenantId, employee.Email);
        var ownerRole = await owner.RoleIdAsync(tenantId, SystemRoles.Owner);
        var ownerMembership = await owner.MembershipIdAsync(tenantId, owner.Email);

        await Problem.AssertAsync(await admin.PutAsync($"{t}/members/{employeeMembership}/role", new { roleId = ownerRole }),
            HttpStatusCode.Forbidden, "owner_required");
        await Problem.AssertAsync(await admin.PostAsync($"{t}/invitations", new { email = Accounts.NewEmail(), roleId = ownerRole }),
            HttpStatusCode.Forbidden, "owner_required");
        await Problem.AssertAsync(await admin.PostAsync($"{t}/members/{ownerMembership}/disable"),
            HttpStatusCode.Forbidden, "owner_required");
        await Problem.AssertAsync(await admin.PostAsync($"{t}/roles",
                new { name = "Facturación", permissions = new[] { Permissions.BillingManage } }),
            HttpStatusCode.Forbidden, "permission_escalation");

        // Within their own permissions they can manage the team.
        var managerRole = await owner.RoleIdAsync(tenantId, SystemRoles.Manager);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PutAsync($"{t}/members/{employeeMembership}/role", new { roleId = managerRole })).StatusCode);
    }

    [Fact]
    public async Task Unknown_permissions_are_rejected()
    {
        using var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync();

        var response = await owner.PostAsync($"/api/v1/tenants/{tenantId}/roles",
            new { name = "Raro", permissions = new[] { "everything.*" } });

        await Problem.AssertAsync(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Role_names_are_unique_per_company_regardless_of_case()
    {
        using var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync();
        var t = $"/api/v1/tenants/{tenantId}";

        await Problem.EnsureSuccessAsync(await owner.PostAsync($"{t}/roles", new { name = "Cajero", permissions = Array.Empty<string>() }));

        await Problem.AssertAsync(await owner.PostAsync($"{t}/roles", new { name = "  CAJERO ", permissions = Array.Empty<string>() }),
            HttpStatusCode.Conflict, "role_name_taken");
        await Problem.AssertAsync(await owner.PostAsync($"{t}/roles", new { name = "propietario", permissions = Array.Empty<string>() }),
            HttpStatusCode.Conflict, "role_name_taken");
    }

    [Fact]
    public async Task System_roles_cannot_be_deleted_and_owner_cannot_be_edited()
    {
        using var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync();
        using var admin = await factory.AddMemberAsync(owner, tenantId, SystemRoles.Administrator);
        var t = $"/api/v1/tenants/{tenantId}";

        await Problem.AssertAsync(await owner.DeleteAsync($"{t}/roles/{await owner.RoleIdAsync(tenantId, SystemRoles.Employee)}"),
            HttpStatusCode.UnprocessableEntity, "role_not_deletable");
        await Problem.AssertAsync(await admin.PutAsync($"{t}/roles/{await owner.RoleIdAsync(tenantId, SystemRoles.Owner)}",
            new { name = "Dueño", permissions = Array.Empty<string>() }), HttpStatusCode.UnprocessableEntity, "role_not_editable");
    }

    [Fact]
    public async Task A_role_in_use_cannot_be_deleted()
    {
        using var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync();
        var t = $"/api/v1/tenants/{tenantId}";
        var role = (await owner.PostJsonAsync<JsonElement>($"{t}/roles",
            new { name = "Cajero", permissions = new[] { Permissions.SalesCreate } })).GetProperty("id").GetGuid();
        using var cashier = await factory.SignedInUserAsync();
        await cashier.JoinAsync(factory, owner, tenantId, role);

        await Problem.AssertAsync(await owner.DeleteAsync($"{t}/roles/{role}"), HttpStatusCode.Conflict, "role_in_use");

        await owner.PutAsync($"{t}/members/{await owner.MembershipIdAsync(tenantId, cashier.Email)}/role",
            new { roleId = await owner.RoleIdAsync(tenantId, SystemRoles.Employee) });
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{t}/roles/{role}")).StatusCode);
    }

    [Fact]
    public async Task The_last_owner_can_neither_leave_nor_be_demoted()
    {
        using var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync();
        using var coOwner = await factory.AddMemberAsync(owner, tenantId, SystemRoles.Owner);
        var t = $"/api/v1/tenants/{tenantId}";
        var ownerMembership = await owner.MembershipIdAsync(tenantId, owner.Email);
        var adminRole = await owner.RoleIdAsync(tenantId, SystemRoles.Administrator);

        // With two owners, one may demote the other…
        Assert.Equal(HttpStatusCode.NoContent,
            (await coOwner.PutAsync($"{t}/members/{ownerMembership}/role", new { roleId = adminRole })).StatusCode);

        // …but the remaining one cannot leave, and nobody can be left without an Owner.
        await Problem.AssertAsync(await coOwner.PostAsync($"{t}/leave"), HttpStatusCode.UnprocessableEntity, "last_owner");
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"{t}/leave")).StatusCode);
        await TenancyKit.AssertTenantNotFoundAsync(await owner.GetAsync(t));
    }

    [Fact]
    public async Task Disabled_members_lose_access_immediately_and_can_be_re_enabled()
    {
        using var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync();
        using var employee = await factory.AddMemberAsync(owner, tenantId, SystemRoles.Employee);
        var t = $"/api/v1/tenants/{tenantId}";
        var membership = await owner.MembershipIdAsync(tenantId, employee.Email);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"{t}/members/{membership}/disable")).StatusCode);
        await TenancyKit.AssertTenantNotFoundAsync(await employee.GetAsync(t));
        Assert.DoesNotContain((await employee.GetJsonAsync<JsonElement>("/api/v1/tenants")).EnumerateArray(),
            c => c.GetProperty("id").GetGuid() == tenantId);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"{t}/members/{membership}/enable")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await employee.GetAsync(t)).StatusCode);
    }

    [Fact]
    public async Task Team_changes_are_audited_in_the_company()
    {
        using var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync();
        using var employee = await factory.AddMemberAsync(owner, tenantId, SystemRoles.Employee);
        await owner.PutAsync($"/api/v1/tenants/{tenantId}/members/{await owner.MembershipIdAsync(tenantId, employee.Email)}/role",
            new { roleId = await owner.RoleIdAsync(tenantId, SystemRoles.Manager) });

        var actions = await Sql.AsTenantAsync(factory, tenantId,
            "SELECT string_agg(action, ',' ORDER BY occurred_at) FROM audit_log");

        Assert.Equal("tenant.created,invitations.created,members.joined,members.role_changed", actions);
    }
}
