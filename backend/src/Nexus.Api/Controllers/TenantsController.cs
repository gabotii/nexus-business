using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Application.Tenancy;
using Nexus.Domain.Tenancy;

namespace Nexus.Api.Controllers;

/// <summary>Endpoints without a tenant: the caller's companies, creating one, and invitation links.</summary>
[ApiController]
[Route("api/v1")]
public sealed class TenantsController(TenantService tenants, InvitationService invitations) : ControllerBase
{
    /// <summary>Companies the caller is an active member of (company switcher).</summary>
    [HttpGet("tenants")]
    public Task<IReadOnlyList<MyTenantDto>> Mine(CancellationToken ct) => tenants.ListMineAsync(ct);

    /// <summary>Creates a company with the caller as Owner. Requires a verified e-mail address.</summary>
    [HttpPost("tenants")]
    public async Task<CreatedResult> Create(CreateTenantRequest request, CancellationToken ct)
    {
        var created = await tenants.CreateAsync(request, ct);
        return Created($"/api/v1/tenants/{created.Id}", created);
    }

    /// <summary>What an invitation link offers (company, role, inviter). Unknown, used or expired links answer 404.</summary>
    [HttpPost("invitations/preview")]
    [AllowAnonymous]
    public Task<InvitationPreviewDto> PreviewInvitation(InvitationTokenRequest request, CancellationToken ct) =>
        invitations.PreviewAsync(request, ct);

    /// <summary>Joins the company. The signed-in account must have the invited, verified e-mail address.</summary>
    [HttpPost("invitations/accept")]
    public Task<MyTenantDto> AcceptInvitation(InvitationTokenRequest request, CancellationToken ct) =>
        invitations.AcceptAsync(request, ct);

    /// <summary>The permission catalog, in display order (ADR-0005).</summary>
    [HttpGet("permissions")]
    public IReadOnlyList<string> PermissionCatalog() => Permissions.All;
}
