using Nexus.Domain.Common;

namespace Nexus.Domain.Tenancy;

/// <summary>
/// An e-mailed offer to join a company with a given role. Only the SHA-256 of the token is stored; the token itself
/// travels once, in the e-mail link.
/// </summary>
public sealed class Invitation : ITenantOwned
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private Invitation()
    {
        Email = NormalizedEmail = string.Empty;
        TokenHash = [];
    }

    public Invitation(Guid tenantId, string email, string normalizedEmail, Guid roleId, byte[] tokenHash, Guid invitedBy,
        DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        Email = email.Trim();
        NormalizedEmail = normalizedEmail;
        RoleId = roleId;
        TokenHash = tokenHash;
        InvitedBy = invitedBy;
        CreatedAt = now;
        ExpiresAt = now + Lifetime;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Email { get; private set; }

    public string NormalizedEmail { get; private set; }

    public Guid RoleId { get; private set; }

    public byte[] TokenHash { get; private set; }

    public Guid InvitedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public Guid? AcceptedBy { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Neither accepted nor revoked. It may still have expired.</summary>
    public bool IsOpen => AcceptedAt is null && RevokedAt is null;

    public bool IsPending(DateTimeOffset now) => IsOpen && ExpiresAt > now;

    public void Accept(Guid userId, DateTimeOffset now)
    {
        if (!IsPending(now))
        {
            throw new DomainException("invitation_not_pending", "La invitación ya no es válida.");
        }

        AcceptedAt = now;
        AcceptedBy = userId;
    }

    public void Revoke(DateTimeOffset now)
    {
        if (!IsOpen)
        {
            throw new DomainException("invitation_not_pending", "La invitación ya no es válida.");
        }

        RevokedAt = now;
    }

    /// <summary>New token and a fresh expiry; the previous link stops working.</summary>
    public void Renew(byte[] tokenHash, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            throw new DomainException("invitation_not_pending", "La invitación ya no es válida.");
        }

        TokenHash = tokenHash;
        ExpiresAt = now + Lifetime;
    }
}
