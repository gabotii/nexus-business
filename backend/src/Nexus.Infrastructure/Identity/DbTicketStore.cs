using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.Application.Common;
using Nexus.Infrastructure.Common;
using Nexus.Infrastructure.Persistence;

namespace Nexus.Infrastructure.Identity;

public static class SessionClaims
{
    /// <summary>Id of the <c>user_sessions</c> row behind the request. Added on retrieval, never stored.</summary>
    public const string SessionId = "nexus:sid";
}

/// <summary>
/// Marks the sign-in endpoint (R3, session fixation). A session cookie presented to it is revoked and the request is
/// treated as anonymous, so the cookie handler stores a brand-new session (new random key) instead of renewing the
/// presented one.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class StartsNewSessionAttribute : Attribute;

/// <summary>
/// Server-side store for the Identity cookie (ADR-0002). Every authenticated request reads its session row, so a
/// revoked or expired session stops working immediately, on every instance. There is deliberately no cache.
/// </summary>
internal sealed class DbTicketStore(
    IServiceScopeFactory scopes,
    IDataProtectionProvider dataProtection,
    TimeProvider clock,
    ILogger<DbTicketStore> logger) : ITicketStore
{
    /// <summary>How often a session's "last seen" time is refreshed (avoids one write per request).</summary>
    private static readonly TimeSpan LastSeenResolution = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(8);

    private readonly IDataProtector _protector = dataProtection.CreateProtector("Nexus.Sessions.Ticket.v1");

    public Task<string> StoreAsync(AuthenticationTicket ticket) => StoreAsync(ticket, null, CancellationToken.None);

    public Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken) =>
        StoreAsync(ticket, null, cancellationToken);

    public async Task<string> StoreAsync(AuthenticationTicket ticket, HttpContext? httpContext,
        CancellationToken cancellationToken)
    {
        var key = SecureTokens.Base64Url(RandomNumberGenerator.GetBytes(32));
        var now = clock.GetUtcNow();
        var session = new UserSession
        {
            Id = Guid.CreateVersion7(),
            UserId = UserIdOf(ticket),
            KeyHash = Hash(key),
            Ticket = Protect(ticket),
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = ticket.Properties.ExpiresUtc ?? now + DefaultLifetime,
            UserAgent = Truncate(httpContext?.Request.Headers.UserAgent.ToString()),
            IpPrefix = IpPrefix.From(httpContext?.Connection.RemoteIpAddress),
        };

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.UserSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) => RenewAsync(key, ticket, null, CancellationToken.None);

    public Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken) =>
        RenewAsync(key, ticket, null, cancellationToken);

    /// <summary>
    /// Sliding expiration, security-stamp refreshes and re-sign-ins of the same user update the row. A renewal for a
    /// different user (signing in over somebody else's session) revokes the session instead, so a session key can
    /// never change hands (session fixation).
    /// </summary>
    public async Task RenewAsync(string key, AuthenticationTicket ticket, HttpContext? httpContext,
        CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hash = Hash(key);
        var session = await db.UserSessions.SingleOrDefaultAsync(s => s.KeyHash == hash && s.RevokedAt == null,
            cancellationToken);
        if (session is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        if (session.UserId != UserIdOf(ticket))
        {
            logger.LogWarning("Session {SessionId} renewed for a different user; revoking it", session.Id);
            session.RevokedAt = now;
        }
        else
        {
            session.Ticket = Protect(ticket);
            session.ExpiresAt = ticket.Properties.ExpiresUtc ?? now + DefaultLifetime;
            session.LastSeenAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => RetrieveAsync(key, null, CancellationToken.None);

    public Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken) =>
        RetrieveAsync(key, null, cancellationToken);

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, HttpContext? httpContext,
        CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hash = Hash(key);
        var now = clock.GetUtcNow();
        var session = await db.UserSessions
            .SingleOrDefaultAsync(s => s.KeyHash == hash && s.RevokedAt == null && s.ExpiresAt > now, cancellationToken);
        if (session is null)
        {
            return null;
        }

        if (httpContext?.GetEndpoint()?.Metadata.GetMetadata<StartsNewSessionAttribute>() is not null)
        {
            session.RevokedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        AuthenticationTicket? ticket;
        try
        {
            ticket = TicketSerializer.Default.Deserialize(_protector.Unprotect(session.Ticket));
        }
        catch (CryptographicException ex)
        {
            // Key ring lost or rotated out: the session cannot be trusted any more.
            logger.LogWarning(ex, "Session {SessionId} could not be decrypted; treating it as signed out", session.Id);
            return null;
        }

        if (ticket?.Principal.Identity is not ClaimsIdentity identity)
        {
            return null;
        }

        identity.AddClaim(new Claim(SessionClaims.SessionId, session.Id.ToString()));

        if (now - session.LastSeenAt > LastSeenResolution)
        {
            session.LastSeenAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        return ticket;
    }

    public Task RemoveAsync(string key) => RemoveAsync(key, null, CancellationToken.None);

    public Task RemoveAsync(string key, CancellationToken cancellationToken) => RemoveAsync(key, null, cancellationToken);

    /// <summary>Sign-out: the row is kept (revoked) for the session history and removed later by housekeeping.</summary>
    public async Task RemoveAsync(string key, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hash = Hash(key);
        await db.UserSessions
            .Where(s => s.KeyHash == hash && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, clock.GetUtcNow()), cancellationToken);
    }

    private byte[] Protect(AuthenticationTicket ticket)
    {
        // The session id claim is request metadata, re-added on every retrieval: store a copy without it (the
        // request's own principal is left untouched).
        var principal = new ClaimsPrincipal(ticket.Principal.Identities.Select(identity =>
        {
            var copy = identity.Clone();
            foreach (var claim in copy.FindAll(SessionClaims.SessionId).ToList())
            {
                copy.RemoveClaim(claim);
            }

            return copy;
        }));

        return _protector.Protect(TicketSerializer.Default.Serialize(
            new AuthenticationTicket(principal, ticket.Properties, ticket.AuthenticationScheme)));
    }

    private static Guid UserIdOf(AuthenticationTicket ticket) =>
        Guid.TryParse(ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new InvalidOperationException("The authentication ticket has no user id.");

    internal static byte[] Hash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));

    private static string? Truncate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null
        : value.Length <= UserSession.UserAgentMaxLength ? value
        : value[..UserSession.UserAgentMaxLength];
}
