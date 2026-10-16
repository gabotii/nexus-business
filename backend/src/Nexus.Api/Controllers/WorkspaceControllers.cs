using Microsoft.AspNetCore.Mvc;
using Nexus.Api.Web;
using Nexus.Application.Tenancy;

namespace Nexus.Api.Controllers;

/// <summary>The opened company: its settings summary and the caller's role and permissions.</summary>
[Route(Prefix)]
public sealed class WorkspaceController(TenantService tenants) : TenantControllerBase
{
    [HttpGet]
    public Task<WorkspaceDto> Get(CancellationToken ct) => tenants.GetWorkspaceAsync(ct);

    /// <summary>The caller leaves the company (refused for its last active Owner).</summary>
    [HttpPost("leave")]
    public async Task<NoContentResult> Leave(CancellationToken ct)
    {
        await tenants.LeaveAsync(ct);
        return NoContent();
    }
}

[Route(Prefix + "/members")]
public sealed class MembersController(TeamService team) : TenantControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<MemberDto>> List(CancellationToken ct) => team.ListMembersAsync(ct);

    [HttpPut("{membershipId:guid}/role")]
    public async Task<NoContentResult> ChangeRole(Guid membershipId, ChangeMemberRoleRequest request, CancellationToken ct)
    {
        await team.ChangeMemberRoleAsync(membershipId, request, ct);
        return NoContent();
    }

    [HttpPost("{membershipId:guid}/disable")]
    public async Task<NoContentResult> Disable(Guid membershipId, CancellationToken ct)
    {
        await team.DisableMemberAsync(membershipId, ct);
        return NoContent();
    }

    [HttpPost("{membershipId:guid}/enable")]
    public async Task<NoContentResult> Enable(Guid membershipId, CancellationToken ct)
    {
        await team.EnableMemberAsync(membershipId, ct);
        return NoContent();
    }
}

[Route(Prefix + "/roles")]
public sealed class RolesController(TeamService team) : TenantControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<RoleDto>> List(CancellationToken ct) => team.ListRolesAsync(ct);

    [HttpPost]
    public async Task<CreatedResult> Create(SaveRoleRequest request, CancellationToken ct)
    {
        var role = await team.CreateRoleAsync(request, ct);
        return Created($"{Request.Path}/{role.Id}", role);
    }

    [HttpPut("{roleId:guid}")]
    public Task<RoleDto> Update(Guid roleId, SaveRoleRequest request, CancellationToken ct) =>
        team.UpdateRoleAsync(roleId, request, ct);

    [HttpDelete("{roleId:guid}")]
    public async Task<NoContentResult> Delete(Guid roleId, CancellationToken ct)
    {
        await team.DeleteRoleAsync(roleId, ct);
        return NoContent();
    }
}

[Route(Prefix + "/invitations")]
public sealed class InvitationsController(InvitationService invitations) : TenantControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<InvitationDto>> List(CancellationToken ct) => invitations.ListAsync(ct);

    [HttpPost]
    public async Task<CreatedResult> Create(CreateInvitationRequest request, CancellationToken ct)
    {
        var invitation = await invitations.CreateAsync(request, ct);
        return Created($"{Request.Path}/{invitation.Id}", invitation);
    }

    /// <summary>New link and expiry; the previous link stops working.</summary>
    [HttpPost("{invitationId:guid}/resend")]
    public async Task<NoContentResult> Resend(Guid invitationId, CancellationToken ct)
    {
        await invitations.ResendAsync(invitationId, ct);
        return NoContent();
    }

    [HttpDelete("{invitationId:guid}")]
    public async Task<NoContentResult> Revoke(Guid invitationId, CancellationToken ct)
    {
        await invitations.RevokeAsync(invitationId, ct);
        return NoContent();
    }
}
