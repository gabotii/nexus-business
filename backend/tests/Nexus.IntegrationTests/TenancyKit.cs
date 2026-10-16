using System.Net;
using System.Text.Json;

namespace Nexus.IntegrationTests;

/// <summary>Companies, roles and members built through the public API, as a user would.</summary>
public static class TenancyKit
{
    public static async Task<Guid> CreateCompanyAsync(this ApiSession owner, string name = "Panadería Núñez")
    {
        var created = await owner.PostJsonAsync<JsonElement>("/api/v1/tenants", new { name });
        return created.GetProperty("id").GetGuid();
    }

    public static async Task<Guid> RoleIdAsync(this ApiSession session, Guid tenantId, string systemKey)
    {
        var roles = await session.GetJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/roles");
        return roles.EnumerateArray().Single(r => r.GetProperty("systemKey").GetString() == systemKey).GetProperty("id").GetGuid();
    }

    /// <summary>Invites a new verified user with the given system role and has them accept.</summary>
    public static async Task<ApiSession> AddMemberAsync(this ApiFactory factory, ApiSession owner, Guid tenantId,
        string systemKey, string name = "Carlos Díaz")
    {
        var member = await factory.SignedInUserAsync(name: name);
        await member.JoinAsync(factory, owner, tenantId, await owner.RoleIdAsync(tenantId, systemKey));
        return member;
    }

    public static async Task JoinAsync(this ApiSession member, ApiFactory factory, ApiSession inviter, Guid tenantId, Guid roleId)
    {
        await Problem.EnsureSuccessAsync(await inviter.PostAsync($"/api/v1/tenants/{tenantId}/invitations",
            new { email = member.Email, roleId }));
        var link = await factory.LatestEmailToAsync(member.Email);
        await Problem.EnsureSuccessAsync(await member.PostAsync("/api/v1/invitations/accept",
            new { tenantId = link.Query("empresa"), token = link.Query("token") }));
    }

    public static async Task<Guid> MembershipIdAsync(this ApiSession session, Guid tenantId, string email)
    {
        var members = await session.GetJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/members");
        return members.EnumerateArray().Single(m => m.GetProperty("email").GetString() == email).GetProperty("id").GetGuid();
    }

    public static async Task AssertTenantNotFoundAsync(HttpResponseMessage response) =>
        await Problem.AssertAsync(response, HttpStatusCode.NotFound, "tenant_not_found");
}
