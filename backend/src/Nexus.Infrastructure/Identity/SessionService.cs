using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.Application.Abstractions;
using Nexus.Application.Common;
using Nexus.Infrastructure.Persistence;

namespace Nexus.Infrastructure.Identity;

/// <summary>The caller's signed-in devices: list and revoke (ADR-0002). Revocation takes effect on the next request.</summary>
public sealed class SessionService(AppDbContext db, IAuditLog audit, TimeProvider clock)
{
    public async Task<IReadOnlyList<SessionDto>> ListAsync(Guid userId, Guid? currentSessionId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return await db.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastSeenAt)
            .Select(s => new SessionDto(s.Id, s.CreatedAt, s.LastSeenAt, s.ExpiresAt, s.UserAgent, s.IpPrefix,
                s.Id == currentSessionId))
            .ToListAsync(ct);
    }

    public async Task RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var revoked = await db.UserSessions
            .Where(s => s.Id == sessionId && s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, clock.GetUtcNow()), ct);
        if (revoked == 0)
        {
            throw new NotFoundException("La sesión no existe o ya está cerrada.");
        }

        audit.Account("auth.session_revoked", userId, new { SessionId = sessionId });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>"Sign out everywhere else", requested by the user.</summary>
    public async Task<int> SignOutOtherDevicesAsync(Guid userId, Guid? currentSessionId, CancellationToken ct)
    {
        var revoked = await RevokeOthersAsync(userId, currentSessionId, ct);
        audit.Account("auth.sessions_revoked", userId, new { Revoked = revoked });
        await db.SaveChangesAsync(ct);
        return revoked;
    }

    /// <returns>The number of sessions revoked.</returns>
    public Task<int> RevokeOthersAsync(Guid userId, Guid? keepSessionId, CancellationToken ct) =>
        db.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.Id != keepSessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, clock.GetUtcNow()), ct);

    public Task<int> RevokeAllAsync(Guid userId, CancellationToken ct) => RevokeOthersAsync(userId, null, ct);
}

/// <summary>Password-reset tokens live 1 hour; e-mail confirmation tokens use the default provider (24 hours).</summary>
internal sealed class PasswordResetTokenProvider(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<PasswordResetTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<AppUser>> logger)
    : DataProtectorTokenProvider<AppUser>(dataProtectionProvider, options, logger)
{
    public const string ProviderName = "PasswordReset";
}

internal sealed class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public PasswordResetTokenProviderOptions()
    {
        Name = PasswordResetTokenProvider.ProviderName;
        TokenLifespan = TimeSpan.FromHours(1);
    }
}
