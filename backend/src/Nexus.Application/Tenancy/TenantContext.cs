using Nexus.Application.Common;

namespace Nexus.Application.Tenancy;

/// <summary>
/// The company a request (or a system process) acts on. The tenant query filter, the write guard and the
/// Row-Level Security session variables all read <see cref="TenantId"/>; <see cref="Guid.Empty"/> means "no tenant",
/// which makes every tenant-owned query return nothing (fail closed).
/// </summary>
public interface ITenantContext
{
    Guid TenantId { get; }

    /// <summary>True once the caller's active membership in <see cref="TenantId"/> has been verified.</summary>
    bool IsMember { get; }

    Guid MembershipId { get; }

    Guid RoleId { get; }

    IReadOnlySet<string> Permissions { get; }

    bool Has(string permission);

    /// <summary>Throws <see cref="ForbiddenException"/> unless the caller's role grants <paramref name="permission"/>.</summary>
    void Demand(string permission);
}

/// <summary>Scoped (one per request or unit of work). Set only by <see cref="TenantAccessService"/> and system scopes.</summary>
public sealed class TenantContext : ITenantContext
{
    private static readonly IReadOnlySet<string> NoPermissions = new HashSet<string>();

    public Guid TenantId { get; private set; }

    public bool IsMember { get; private set; }

    public Guid MembershipId { get; private set; }

    public Guid RoleId { get; private set; }

    public IReadOnlySet<string> Permissions { get; private set; } = NoPermissions;

    public bool Has(string permission) => IsMember && Permissions.Contains(permission);

    public void Demand(string permission)
    {
        if (!Has(permission))
        {
            throw new ForbiddenException("permission_required", "No tienes permiso para realizar esta acción.");
        }
    }

    /// <summary>
    /// Acts on <paramref name="tenantId"/> without a membership: creating a company, accepting an invitation, and
    /// (later) webhooks and background jobs. The previous state is restored on dispose. Callers must open the scope
    /// before the database connection they use is opened, because RLS variables are applied on connection open.
    /// </summary>
    public IDisposable BeginSystemScope(Guid tenantId)
    {
        var previous = (TenantId, IsMember, MembershipId, RoleId, Permissions);
        TenantId = tenantId;
        IsMember = false;
        MembershipId = Guid.Empty;
        RoleId = Guid.Empty;
        Permissions = NoPermissions;
        return new Restore(() => (TenantId, IsMember, MembershipId, RoleId, Permissions) = previous);
    }

    internal void BeginResolution(Guid tenantId) => TenantId = tenantId;

    internal void SetMembership(Guid membershipId, Guid roleId, IReadOnlySet<string> permissions)
    {
        IsMember = true;
        MembershipId = membershipId;
        RoleId = roleId;
        Permissions = permissions;
    }

    internal void Clear()
    {
        TenantId = Guid.Empty;
        IsMember = false;
        MembershipId = Guid.Empty;
        RoleId = Guid.Empty;
        Permissions = NoPermissions;
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        private Action? _restore = restore;

        public void Dispose()
        {
            _restore?.Invoke();
            _restore = null;
        }
    }
}
