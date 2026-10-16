using Microsoft.AspNetCore.Identity;

namespace Nexus.Infrastructure.Identity;

/// <summary>
/// A person's account (table <c>users</c>). Global: one account may belong to several companies through
/// memberships. Hashing, lockout and security stamps are ASP.NET Core Identity's.
/// </summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public const int DisplayNameMaxLength = 100;

    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A signed-in device (ADR-0002). The cookie holds only a random key; the row stores its SHA-256, so a database
/// leak does not reveal usable session keys, and the authentication ticket encrypted with Data Protection.
/// </summary>
public sealed class UserSession
{
    public const int UserAgentMaxLength = 256;

    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public byte[] KeyHash { get; set; } = [];

    public byte[] Ticket { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? UserAgent { get; set; }

    public string? IpPrefix { get; set; }
}
