using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Nexus.Application.Abstractions;
using Nexus.Application.Common;
using Nexus.Domain.Common;
using Nexus.Domain.Tenancy;

namespace Nexus.Application.Tenancy;

/// <summary>
/// Members and roles of the current company, with the anti-escalation rules of ADR-0005 enforced here (the API
/// and the SPA only mirror them):
/// <list type="bullet">
/// <item>nobody changes their own role, disables themselves or edits the role they hold;</item>
/// <item>a caller only manages members, roles and invitations whose permissions are a subset of their own;</item>
/// <item>only an Owner grants or removes the Owner role;</item>
/// <item>the company always keeps at least one active Owner (checked under a tenant row lock).</item>
/// </list>
/// </summary>
public sealed class TeamService(
    IAppDbContext db,
    IDatabaseTools tools,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IUserDirectory users,
    IAuditLog audit,
    IValidator<SaveRoleRequest> roleValidator,
    TimeProvider clock)
{
    internal const string RoleNameConstraint = "ux_roles_tenant_name";

    public async Task<IReadOnlyList<MemberDto>> ListMembersAsync(CancellationToken ct)
    {
        tenant.Demand(Permissions.MembersView);
        var rows = await db.Memberships
            .Join(db.Roles, m => m.RoleId, r => r.Id, (m, r) => new { Membership = m, Role = r })
            .ToListAsync(ct);
        var accounts = await users.GetManyAsync(rows.Select(r => r.Membership.UserId), ct);

        return rows
            .Select(r =>
            {
                var account = accounts.GetValueOrDefault(r.Membership.UserId);
                return new MemberDto(r.Membership.Id, r.Membership.UserId, account?.DisplayName ?? "—",
                    account?.Email ?? "—", new RoleRef(r.Role.Id, r.Role.Name, r.Role.SystemKey), r.Membership.Status,
                    r.Membership.JoinedAt, r.Membership.UserId == currentUser.UserId);
            })
            .OrderBy(m => m.Status)
            .ThenBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task ChangeMemberRoleAsync(Guid membershipId, ChangeMemberRoleRequest request, CancellationToken ct)
    {
        tenant.Demand(Permissions.MembersManage);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await tools.LockTenantAsync(tenant.TenantId, ct);

        var (member, currentRole) = await LoadManageableMemberAsync(membershipId, ct);
        var newRole = await db.Roles.Include(r => r.Permissions).SingleOrDefaultAsync(r => r.Id == request.RoleId, ct)
                      ?? throw new NotFoundException("El rol no existe.");
        if (newRole.Id == currentRole.Id)
        {
            return;
        }

        await EnsureCanGrantAsync(newRole, ct);
        member.ChangeRole(newRole.Id);
        if (currentRole.IsOwner && member.IsActive)
        {
            await EnsureAnotherOwnerRemainsAsync(ct);
        }

        audit.Tenant("members.role_changed", "membership", member.Id,
            new { FromRoleId = currentRole.Id, ToRoleId = newRole.Id });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task DisableMemberAsync(Guid membershipId, CancellationToken ct)
    {
        tenant.Demand(Permissions.MembersManage);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await tools.LockTenantAsync(tenant.TenantId, ct);

        var (member, role) = await LoadManageableMemberAsync(membershipId, ct);
        member.Disable(clock.GetUtcNow());
        if (role.IsOwner)
        {
            await EnsureAnotherOwnerRemainsAsync(ct);
        }

        audit.Tenant("members.disabled", "membership", member.Id);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task EnableMemberAsync(Guid membershipId, CancellationToken ct)
    {
        tenant.Demand(Permissions.MembersManage);
        var (member, role) = await LoadManageableMemberAsync(membershipId, ct);
        if (member.IsActive)
        {
            return;
        }

        member.Enable(role.Id, clock.GetUtcNow());
        audit.Tenant("members.enabled", "membership", member.Id);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct)
    {
        tenant.Demand(Permissions.MembersView);
        var roles = await db.Roles.Include(r => r.Permissions).ToListAsync(ct);
        var counts = await db.Memberships.Where(m => m.Status == MembershipStatus.Active)
            .GroupBy(m => m.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.RoleId, g => g.Count, ct);

        return roles
            .OrderBy(r => r.IsSystem ? SystemOrder(r.SystemKey!) : int.MaxValue)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(r => ToDto(r, counts.GetValueOrDefault(r.Id)))
            .ToList();
    }

    public async Task<RoleDto> CreateRoleAsync(SaveRoleRequest request, CancellationToken ct)
    {
        tenant.Demand(Permissions.RolesManage);
        await roleValidator.ValidateAndThrowAsync(request, ct);
        EnsureWithinOwnPermissions(request.Permissions!);
        await EnsureRoleNameFreeAsync(request.Name, exceptRoleId: null, ct);

        var role = Role.CreateCustom(tenant.TenantId, request.Name, request.Description, request.Permissions!,
            clock.GetUtcNow());
        db.Roles.Add(role);
        audit.Tenant("roles.created", "role", role.Id, new { Permissions = role.EffectivePermissions.Order() });
        await SaveRoleChangesAsync(ct);
        return ToDto(role, 0);
    }

    public async Task<RoleDto> UpdateRoleAsync(Guid roleId, SaveRoleRequest request, CancellationToken ct)
    {
        tenant.Demand(Permissions.RolesManage);
        await roleValidator.ValidateAndThrowAsync(request, ct);
        var role = await db.Roles.Include(r => r.Permissions).SingleOrDefaultAsync(r => r.Id == roleId, ct)
                   ?? throw new NotFoundException("El rol no existe.");
        if (role.IsOwner)
        {
            throw new DomainException("role_not_editable", "El rol Propietario no se puede modificar.");
        }

        if (role.Id == tenant.RoleId)
        {
            throw new ForbiddenException("cannot_modify_self", "No puedes modificar el rol que tienes asignado.");
        }

        EnsureWithinOwnPermissions(role.EffectivePermissions);
        EnsureWithinOwnPermissions(request.Permissions!);
        await EnsureRoleNameFreeAsync(request.Name, role.Id, ct);

        var before = role.EffectivePermissions;
        role.Update(request.Name, request.Description, request.Permissions!);
        var after = role.EffectivePermissions;
        audit.Tenant("roles.updated", "role", role.Id, new
        {
            Added = after.Except(before).Order(),
            Removed = before.Except(after).Order(),
        });
        await SaveRoleChangesAsync(ct);

        var members = await db.Memberships.CountAsync(m => m.RoleId == role.Id && m.Status == MembershipStatus.Active, ct);
        return ToDto(role, members);
    }

    public async Task DeleteRoleAsync(Guid roleId, CancellationToken ct)
    {
        tenant.Demand(Permissions.RolesManage);
        var role = await db.Roles.Include(r => r.Permissions).SingleOrDefaultAsync(r => r.Id == roleId, ct)
                   ?? throw new NotFoundException("El rol no existe.");
        if (role.IsSystem)
        {
            throw new DomainException("role_not_deletable", "Los roles predefinidos no se pueden eliminar.");
        }

        EnsureWithinOwnPermissions(role.EffectivePermissions);
        var inUse = await db.Memberships.AnyAsync(m => m.RoleId == roleId, ct) ||
                    await db.Invitations.AnyAsync(i => i.RoleId == roleId && i.AcceptedAt == null && i.RevokedAt == null, ct);
        if (inUse)
        {
            throw new ConflictException("role_in_use",
                "El rol está asignado a miembros o invitaciones. Reasígnalos antes de eliminarlo.");
        }

        // Accepted or revoked invitations still point at the role; they are spent, and the audit log keeps the trail.
        db.Invitations.RemoveRange(await db.Invitations.Where(i => i.RoleId == roleId).ToListAsync(ct));
        db.Roles.Remove(role);
        audit.Tenant("roles.deleted", "role", role.Id, new { role.Name });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Loads a member the caller may manage: not themselves, holding a role within the caller's permissions, and
    /// (for Owners) only when the caller is an Owner too.
    /// </summary>
    private async Task<(Membership Member, Role Role)> LoadManageableMemberAsync(Guid membershipId, CancellationToken ct)
    {
        var member = await db.Memberships.SingleOrDefaultAsync(m => m.Id == membershipId, ct)
                     ?? throw new NotFoundException("El miembro no existe.");
        if (member.Id == tenant.MembershipId)
        {
            throw new ForbiddenException("cannot_modify_self", "No puedes cambiar tu propio rol ni desactivarte.");
        }

        var role = await db.Roles.Include(r => r.Permissions).SingleAsync(r => r.Id == member.RoleId, ct);
        await EnsureCanGrantAsync(role, ct);
        return (member, role);
    }

    /// <summary>The caller may hand out (or take away) <paramref name="role"/>.</summary>
    internal async Task EnsureCanGrantAsync(Role role, CancellationToken ct)
    {
        if (role.IsOwner && !await TeamRules.IsOwnerAsync(db, tenant.RoleId, ct))
        {
            throw new ForbiddenException("owner_required", "Solo un propietario puede asignar o retirar el rol Propietario.");
        }

        EnsureWithinOwnPermissions(role.EffectivePermissions);
    }

    private void EnsureWithinOwnPermissions(IEnumerable<string> permissions)
    {
        if (!permissions.All(tenant.Permissions.Contains))
        {
            throw new ForbiddenException("permission_escalation",
                "No puedes conceder ni gestionar permisos que tú no tienes.");
        }
    }

    private async Task EnsureAnotherOwnerRemainsAsync(CancellationToken ct)
    {
        // The pending change is not saved yet: count the Owners that will remain active after it.
        await db.SaveChangesAsync(ct);
        if (await TeamRules.ActiveOwnerCountAsync(db, ct) < 1)
        {
            throw new DomainException("last_owner", "La empresa debe conservar al menos un propietario activo.");
        }
    }

    private async Task EnsureRoleNameFreeAsync(string name, Guid? exceptRoleId, CancellationToken ct)
    {
        var normalized = name.Trim().ToLower(System.Globalization.CultureInfo.InvariantCulture);
        // Translated to SQL lower(), the same expression as the ux_roles_tenant_name index.
#pragma warning disable CA1304, CA1311, CA1862
        if (await db.Roles.AnyAsync(r => r.Id != exceptRoleId && r.Name.ToLower() == normalized, ct))
#pragma warning restore CA1304, CA1311, CA1862
        {
            throw RoleNameTaken();
        }
    }

    private async Task SaveRoleChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (tools.IsUniqueViolation(ex, RoleNameConstraint))
        {
            throw RoleNameTaken();
        }
    }

    private static ConflictException RoleNameTaken() =>
        new("role_name_taken", "Ya existe un rol con ese nombre.");

    private static int SystemOrder(string key) =>
        SystemRoles.Defaults.Select((d, i) => (d.Key, i)).Single(x => x.Key == key).i;

    private static RoleDto ToDto(Role role, int members) => new(
        role.Id, role.Name, role.Description, role.SystemKey,
        Editable: !role.IsOwner,
        Deletable: !role.IsSystem,
        [.. Permissions.All.Where(role.EffectivePermissions.Contains)],
        members);
}
