using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Nexus.Application.Abstractions;
using Nexus.Application.Common;
using Nexus.Domain.Tenancy;

namespace Nexus.Application.Tenancy;

/// <summary>
/// Invitations to join the current company. The link carries the tenant id and a 256-bit secret; only the secret's
/// hash is stored. Accepting requires a signed-in account with a verified e-mail equal to the invited address.
/// </summary>
public sealed class InvitationService(
    IAppDbContext db,
    IDatabaseTools tools,
    TenantContext tenant,
    TeamService team,
    ICurrentUser currentUser,
    IUserDirectory users,
    IAuditLog audit,
    IEmailOutbox outbox,
    IAppLinks links,
    IValidator<CreateInvitationRequest> createValidator,
    IValidator<InvitationTokenRequest> tokenValidator,
    TimeProvider clock)
{
    internal const string OpenInvitationConstraint = "ux_invitations_open_email";

    public async Task<IReadOnlyList<InvitationDto>> ListAsync(CancellationToken ct)
    {
        tenant.Demand(Permissions.MembersView);
        var now = clock.GetUtcNow();
        var rows = await db.Invitations
            .Where(i => i.AcceptedAt == null && i.RevokedAt == null)
            .Join(db.Roles, i => i.RoleId, r => r.Id, (i, r) => new { Invitation = i, Role = r })
            .OrderByDescending(x => x.Invitation.CreatedAt)
            .ToListAsync(ct);
        var inviters = await users.GetManyAsync(rows.Select(r => r.Invitation.InvitedBy), ct);

        return rows.Select(x => new InvitationDto(
                x.Invitation.Id, x.Invitation.Email, new RoleRef(x.Role.Id, x.Role.Name, x.Role.SystemKey),
                x.Invitation.IsPending(now) ? InvitationState.Pending : InvitationState.Expired,
                x.Invitation.CreatedAt, x.Invitation.ExpiresAt,
                inviters.GetValueOrDefault(x.Invitation.InvitedBy)?.DisplayName ?? "—"))
            .ToList();
    }

    public async Task<InvitationDto> CreateAsync(CreateInvitationRequest request, CancellationToken ct)
    {
        tenant.Demand(Permissions.MembersManage);
        await createValidator.ValidateAndThrowAsync(request, ct);

        var role = await db.Roles.Include(r => r.Permissions).SingleOrDefaultAsync(r => r.Id == request.RoleId, ct)
                   ?? throw new NotFoundException("El rol no existe.");
        await team.EnsureCanGrantAsync(role, ct);

        var email = request.Email.Trim();
        var normalized = users.NormalizeEmail(email);
        if (await users.FindIdByEmailAsync(email, ct) is { } existingUser &&
            await db.Memberships.AnyAsync(m => m.UserId == existingUser && m.Status == MembershipStatus.Active, ct))
        {
            throw new ConflictException("already_member", "Esa persona ya es miembro de la empresa.");
        }

        var now = clock.GetUtcNow();
        var open = await db.Invitations.SingleOrDefaultAsync(
            i => i.NormalizedEmail == normalized && i.AcceptedAt == null && i.RevokedAt == null, ct);
        if (open is not null)
        {
            if (open.IsPending(now))
            {
                throw new ConflictException("invitation_pending",
                    "Ya hay una invitación pendiente para ese correo. Puedes reenviarla.");
            }

            open.Revoke(now);
        }

        var (token, hash) = SecureTokens.Create();
        var invitation = new Invitation(tenant.TenantId, email, normalized, role.Id, hash, currentUser.UserId, now);
        db.Invitations.Add(invitation);
        await QueueEmailAsync(invitation, role, token, ct);
        audit.Tenant("invitations.created", "invitation", invitation.Id, new { RoleId = role.Id });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (tools.IsUniqueViolation(ex, OpenInvitationConstraint))
        {
            throw new ConflictException("invitation_pending", "Ya hay una invitación pendiente para ese correo.");
        }

        var inviter = await users.FindAsync(currentUser.UserId, ct);
        return new InvitationDto(invitation.Id, invitation.Email, new RoleRef(role.Id, role.Name, role.SystemKey),
            InvitationState.Pending, invitation.CreatedAt, invitation.ExpiresAt, inviter?.DisplayName ?? "—");
    }

    /// <summary>Issues a new link (the old one stops working) with a fresh 7-day expiry.</summary>
    public async Task ResendAsync(Guid invitationId, CancellationToken ct)
    {
        tenant.Demand(Permissions.MembersManage);
        var invitation = await FindOpenAsync(invitationId, ct);
        var role = await db.Roles.Include(r => r.Permissions).SingleAsync(r => r.Id == invitation.RoleId, ct);
        await team.EnsureCanGrantAsync(role, ct);

        var (token, hash) = SecureTokens.Create();
        invitation.Renew(hash, clock.GetUtcNow());
        await QueueEmailAsync(invitation, role, token, ct);
        audit.Tenant("invitations.resent", "invitation", invitation.Id);
        await db.SaveChangesAsync(ct);
    }

    public async Task RevokeAsync(Guid invitationId, CancellationToken ct)
    {
        tenant.Demand(Permissions.MembersManage);
        var invitation = await FindOpenAsync(invitationId, ct);
        invitation.Revoke(clock.GetUtcNow());
        audit.Tenant("invitations.revoked", "invitation", invitation.Id);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>What the invitation link offers, shown before signing in. Unknown, used or expired links are 404.</summary>
    public async Task<InvitationPreviewDto> PreviewAsync(InvitationTokenRequest request, CancellationToken ct)
    {
        await tokenValidator.ValidateAndThrowAsync(request, ct);
        using var scope = tenant.BeginSystemScope(request.TenantId);
        var (invitation, company) = await FindPendingByTokenAsync(request, ct);
        var role = await db.Roles.SingleAsync(r => r.Id == invitation.RoleId, ct);
        var inviter = await users.FindAsync(invitation.InvitedBy, ct);
        return new InvitationPreviewDto(company.Name, invitation.Email, role.Name, inviter?.DisplayName ?? "—",
            invitation.ExpiresAt);
    }

    /// <summary>
    /// Joins the company with the invited role (re-activating a previously disabled membership). The invitation is
    /// bound to its e-mail address: a link forwarded to another account does not work.
    /// </summary>
    public async Task<MyTenantDto> AcceptAsync(InvitationTokenRequest request, CancellationToken ct)
    {
        await tokenValidator.ValidateAndThrowAsync(request, ct);
        var user = await users.FindAsync(currentUser.UserId, ct) ?? throw new NotFoundException();
        if (!user.EmailConfirmed)
        {
            throw new ForbiddenException("email_not_verified",
                "Verifica tu correo electrónico antes de aceptar la invitación.");
        }

        using var scope = tenant.BeginSystemScope(request.TenantId);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await tools.LockTenantAsync(request.TenantId, ct);

        var (invitation, company) = await FindPendingByTokenAsync(request, ct);
        if (invitation.NormalizedEmail != users.NormalizeEmail(user.Email))
        {
            throw new ForbiddenException("invitation_email_mismatch",
                $"Esta invitación es para {invitation.Email}. Inicia sesión con esa cuenta para aceptarla.");
        }

        var now = clock.GetUtcNow();
        var membership = await db.Memberships.SingleOrDefaultAsync(m => m.UserId == user.Id, ct);
        if (membership is { IsActive: true })
        {
            throw new ConflictException("already_member", "Ya eres miembro de esta empresa.");
        }

        if (membership is null)
        {
            membership = new Membership(company.Id, user.Id, invitation.RoleId, now);
            db.Memberships.Add(membership);
        }
        else
        {
            membership.Enable(invitation.RoleId, now);
        }

        invitation.Accept(user.Id, now);
        audit.Tenant("members.joined", "membership", membership.Id,
            new { InvitationId = invitation.Id, invitation.RoleId });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var role = await db.Roles.SingleAsync(r => r.Id == invitation.RoleId, ct);
        return new MyTenantDto(company.Id, company.Name, company.Slug, new RoleRef(role.Id, role.Name, role.SystemKey));
    }

    private async Task<Invitation> FindOpenAsync(Guid invitationId, CancellationToken ct) =>
        await db.Invitations.SingleOrDefaultAsync(i => i.Id == invitationId && i.AcceptedAt == null && i.RevokedAt == null, ct)
        ?? throw new NotFoundException("La invitación no existe o ya no está pendiente.");

    private async Task<(Invitation Invitation, Tenant Company)> FindPendingByTokenAsync(InvitationTokenRequest request,
        CancellationToken ct)
    {
        var hash = SecureTokens.Hash(request.Token);
        var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.TokenHash == hash, ct);
        var company = invitation is null
            ? null
            : await db.Tenants.SingleOrDefaultAsync(t => t.Id == request.TenantId && t.Status == TenantStatus.Active, ct);
        if (invitation is null || company is null || !invitation.IsPending(clock.GetUtcNow()))
        {
            throw new NotFoundException("La invitación no existe, ya se usó o ha caducado.");
        }

        return (invitation, company);
    }

    private async Task QueueEmailAsync(Invitation invitation, Role role, string token, CancellationToken ct)
    {
        var company = await db.Tenants.SingleAsync(t => t.Id == invitation.TenantId, ct);
        var inviter = await users.FindAsync(currentUser.UserId, ct);
        outbox.Enqueue(invitation.Email,
            $"Invitación para unirte a {company.Name} en {links.ProductName}",
            $"Hola:\n\n{inviter?.DisplayName ?? "Un miembro del equipo"} te ha invitado a unirte a {company.Name} " +
            $"en {links.ProductName} con el rol {role.Name}.\n\n" +
            $"Para aceptar la invitación abre este enlace (caduca en {Invitation.Lifetime.Days} días):\n" +
            $"{links.Invitation(invitation.TenantId, token)}\n\n" +
            "Si no esperabas esta invitación, puedes ignorar este mensaje.");
    }
}
