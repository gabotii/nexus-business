using System.Net;
using System.Text.Json;
using Nexus.Domain.Tenancy;

namespace Nexus.IntegrationTests;

/// <summary>Joining a company by e-mailed invitation: links bound to the address, single use, revocable.</summary>
[Collection(SharedApi.Name)]
public sealed class InvitationTests(ApiFactory factory)
{
    [Fact]
    public async Task An_invitee_previews_the_link_signs_in_and_joins_with_the_invited_role()
    {
        using var owner = await factory.SignedInUserAsync(name: "Elena Owner");
        var tenantId = await owner.CreateCompanyAsync("Taller Ruiz");
        var managerRole = await owner.RoleIdAsync(tenantId, SystemRoles.Manager);
        var inviteeEmail = Accounts.NewEmail("invitee");

        var invitation = await owner.PostJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/invitations",
            new { email = inviteeEmail, roleId = managerRole });
        Assert.Equal("pending", invitation.GetProperty("state").GetString());

        var mail = await factory.LatestEmailToAsync(inviteeEmail);
        Assert.Contains("Taller Ruiz", mail.Subject, StringComparison.Ordinal);
        Assert.Equal(tenantId.ToString(), mail.Query("empresa"));
        var link = new { tenantId = mail.Query("empresa"), token = mail.Query("token") };

        using var anonymous = new ApiSession(factory);
        var preview = await anonymous.PostJsonAsync<JsonElement>("/api/v1/invitations/preview", link);
        Assert.Equal("Taller Ruiz", preview.GetProperty("tenantName").GetString());
        Assert.Equal("Responsable", preview.GetProperty("roleName").GetString());
        Assert.Equal("Elena Owner", preview.GetProperty("invitedBy").GetString());

        using var invitee = await factory.SignedInUserAsync(inviteeEmail);
        var joined = await invitee.PostJsonAsync<JsonElement>("/api/v1/invitations/accept", link);
        Assert.Equal(tenantId, joined.GetProperty("id").GetGuid());
        Assert.Equal(SystemRoles.Manager, joined.GetProperty("role").GetProperty("systemKey").GetString());
        Assert.Equal(HttpStatusCode.OK, (await invitee.GetAsync($"/api/v1/tenants/{tenantId}/members")).StatusCode);
        Assert.Equal(0, (await owner.GetJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/invitations")).GetArrayLength());

        // Single use.
        await Problem.AssertAsync(await invitee.PostAsync("/api/v1/invitations/accept", link), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task A_forwarded_link_does_not_work_for_another_account()
    {
        using var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync();
        var invitedEmail = Accounts.NewEmail();
        await owner.PostAsync($"/api/v1/tenants/{tenantId}/invitations",
            new { email = invitedEmail, roleId = await owner.RoleIdAsync(tenantId, SystemRoles.Employee) });
        var mail = await factory.LatestEmailToAsync(invitedEmail);
        using var someoneElse = await factory.SignedInUserAsync();

        var response = await someoneElse.PostAsync("/api/v1/invitations/accept",
            new { tenantId = mail.Query("empresa"), token = mail.Query("token") });

        await Problem.AssertAsync(response, HttpStatusCode.Forbidden, "invitation_email_mismatch");
        await TenancyKit.AssertTenantNotFoundAsync(await someoneElse.GetAsync($"/api/v1/tenants/{tenantId}"));
    }

    [Fact]
    public async Task An_unverified_account_cannot_accept()
    {
        using var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync();
        using var unverified = await factory.SignedInUserAsync(verified: false);
        await owner.PostAsync($"/api/v1/tenants/{tenantId}/invitations",
            new { email = unverified.Email, roleId = await owner.RoleIdAsync(tenantId, SystemRoles.Employee) });
        var mail = await factory.LatestEmailToAsync(unverified.Email);

        var response = await unverified.PostAsync("/api/v1/invitations/accept",
            new { tenantId = mail.Query("empresa"), token = mail.Query("token") });

        await Problem.AssertAsync(response, HttpStatusCode.Forbidden, "email_not_verified");
    }

    [Fact]
    public async Task Revoked_and_resent_links_stop_working()
    {
        using var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync();
        using var invitee = await factory.SignedInUserAsync();
        var t = $"/api/v1/tenants/{tenantId}";
        var invitation = await owner.PostJsonAsync<JsonElement>($"{t}/invitations",
            new { email = invitee.Email, roleId = await owner.RoleIdAsync(tenantId, SystemRoles.Employee) });
        var id = invitation.GetProperty("id").GetGuid();
        var firstLink = await factory.LatestEmailToAsync(invitee.Email);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"{t}/invitations/{id}/resend")).StatusCode);
        var secondLink = await factory.LatestEmailToAsync(invitee.Email);
        Assert.NotEqual(firstLink.Query("token"), secondLink.Query("token"));
        await Problem.AssertAsync(await invitee.PostAsync("/api/v1/invitations/accept",
            new { tenantId, token = firstLink.Query("token") }), HttpStatusCode.NotFound, "not_found");

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{t}/invitations/{id}")).StatusCode);
        await Problem.AssertAsync(await invitee.PostAsync("/api/v1/invitations/accept",
            new { tenantId, token = secondLink.Query("token") }), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task A_link_only_works_with_the_company_it_was_issued_for()
    {
        using var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync();
        using var other = await factory.SignedInUserAsync();
        var otherTenant = await other.CreateCompanyAsync();
        using var invitee = await factory.SignedInUserAsync();
        await owner.PostAsync($"/api/v1/tenants/{tenantId}/invitations",
            new { email = invitee.Email, roleId = await owner.RoleIdAsync(tenantId, SystemRoles.Employee) });
        var mail = await factory.LatestEmailToAsync(invitee.Email);

        var response = await invitee.PostAsync("/api/v1/invitations/accept", new { tenantId = otherTenant, token = mail.Query("token") });

        await Problem.AssertAsync(response, HttpStatusCode.NotFound, "not_found");
        await TenancyKit.AssertTenantNotFoundAsync(await invitee.GetAsync($"/api/v1/tenants/{otherTenant}"));
    }

    [Fact]
    public async Task Inviting_an_active_member_or_a_pending_address_is_a_conflict()
    {
        using var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync();
        using var member = await factory.AddMemberAsync(owner, tenantId, SystemRoles.Employee);
        var t = $"/api/v1/tenants/{tenantId}";
        var role = await owner.RoleIdAsync(tenantId, SystemRoles.Employee);
        var pending = Accounts.NewEmail();
        await owner.PostAsync($"{t}/invitations", new { email = pending, roleId = role });

        await Problem.AssertAsync(await owner.PostAsync($"{t}/invitations", new { email = member.Email, roleId = role }),
            HttpStatusCode.Conflict, "already_member");
        await Problem.AssertAsync(await owner.PostAsync($"{t}/invitations", new { email = pending.ToUpperInvariant(), roleId = role }),
            HttpStatusCode.Conflict, "invitation_pending");
    }

    [Fact]
    public async Task A_disabled_member_rejoins_through_a_new_invitation()
    {
        using var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync();
        using var member = await factory.AddMemberAsync(owner, tenantId, SystemRoles.Employee);
        var t = $"/api/v1/tenants/{tenantId}";
        var membership = await owner.MembershipIdAsync(tenantId, member.Email);
        await owner.PostAsync($"{t}/members/{membership}/disable");

        await member.JoinAsync(factory, owner, tenantId, await owner.RoleIdAsync(tenantId, SystemRoles.ReadOnly));

        Assert.Equal(membership, await owner.MembershipIdAsync(tenantId, member.Email));
        var workspace = await member.GetJsonAsync<JsonElement>(t);
        Assert.Equal(SystemRoles.ReadOnly, workspace.GetProperty("role").GetProperty("systemKey").GetString());
    }

    [Fact]
    public async Task Unknown_links_preview_as_not_found()
    {
        using var anonymous = new ApiSession(factory);

        var response = await anonymous.PostAsync("/api/v1/invitations/preview", new { tenantId = Guid.NewGuid(), token = "nope" });

        await Problem.AssertAsync(response, HttpStatusCode.NotFound, "not_found");
    }
}
