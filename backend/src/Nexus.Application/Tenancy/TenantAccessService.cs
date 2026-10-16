using Microsoft.EntityFrameworkCore;
using Nexus.Application.Abstractions;
using Nexus.Domain.Tenancy;

namespace Nexus.Application.Tenancy;

/// <summary>
/// Resolves the tenant of a request from the route (ADR-0003): the caller must hold an active membership in an
/// active company. Every query here already runs under the requested tenant (EF filter + RLS), so a tenant the
/// caller does not belong to simply yields no rows.
/// </summary>
public sealed class TenantAccessService(IAppDbContext db, ICurrentUser currentUser, TenantContext context)
{
    /// <returns>False when the company does not exist, is suspended, or the caller is not an active member.</returns>
    public async Task<bool> ResolveAsync(Guid tenantId, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (userId == Guid.Empty || tenantId == Guid.Empty)
        {
            return false;
        }

        context.BeginResolution(tenantId);

        var membership = await db.Memberships
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active)
            .Join(db.Tenants.Where(t => t.Id == tenantId && t.Status == TenantStatus.Active),
                m => m.TenantId, t => t.Id, (m, _) => new { m.Id, m.RoleId })
            .SingleOrDefaultAsync(ct);

        if (membership is null)
        {
            context.Clear();
            return false;
        }

        var role = await db.Roles
            .Where(r => r.Id == membership.RoleId)
            .Select(r => new { r.SystemKey, Stored = r.Permissions.Select(p => p.Permission).ToList() })
            .SingleAsync(ct);

        context.SetMembership(membership.Id, membership.RoleId, Role.EffectivePermissionsOf(role.SystemKey, role.Stored));
        return true;
    }
}
