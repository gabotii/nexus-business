using Nexus.Domain.Common;

namespace Nexus.Domain.Tenancy;

public enum MembershipStatus
{
    Active,
    Disabled,
}

/// <summary>
/// A user's access to one company with one role. Disabling (never deleting) keeps the history that audit entries
/// and, later, sales refer to.
/// </summary>
public sealed class Membership : ITenantOwned
{
    private Membership()
    {
    }

    public Membership(Guid tenantId, Guid userId, Guid roleId, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        UserId = userId;
        RoleId = roleId;
        Status = MembershipStatus.Active;
        JoinedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }

    public MembershipStatus Status { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    public DateTimeOffset? DisabledAt { get; private set; }

    public uint Version { get; private set; }

    public bool IsActive => Status == MembershipStatus.Active;

    public void ChangeRole(Guid roleId) => RoleId = roleId;

    public void Disable(DateTimeOffset now)
    {
        if (Status == MembershipStatus.Disabled)
        {
            throw new DomainException("member_already_disabled", "El miembro ya está desactivado.");
        }

        Status = MembershipStatus.Disabled;
        DisabledAt = now;
    }

    /// <summary>Reactivates the membership (by an administrator or by accepting a new invitation).</summary>
    public void Enable(Guid roleId, DateTimeOffset now)
    {
        if (Status == MembershipStatus.Disabled)
        {
            JoinedAt = now;
        }

        Status = MembershipStatus.Active;
        DisabledAt = null;
        RoleId = roleId;
    }
}
