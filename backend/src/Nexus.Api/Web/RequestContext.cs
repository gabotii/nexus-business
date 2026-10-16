using System.Security.Claims;
using Nexus.Application.Abstractions;
using Nexus.Infrastructure.Identity;

namespace Nexus.Api.Web;

/// <summary>The caller as established by the session cookie. Anonymous requests and background work get <see cref="Guid.Empty"/>.</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid UserId => accessor.HttpContext?.User.UserId() ?? Guid.Empty;
}

internal sealed class HttpRequestInfo(IHttpContextAccessor accessor) : IRequestInfo
{
    public string? CorrelationId => accessor.HttpContext is { } context ? Correlation.Id(context) : null;

    public string? IpPrefix => Infrastructure.Common.IpPrefix.From(accessor.HttpContext?.Connection.RemoteIpAddress);
}

internal static class PrincipalExtensions
{
    public static Guid UserId(this ClaimsPrincipal principal) =>
        principal.Identity?.IsAuthenticated == true &&
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : Guid.Empty;

    /// <summary>The <c>user_sessions</c> row behind this request, when it came with a session cookie.</summary>
    public static Guid? SessionId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(SessionClaims.SessionId), out var id) ? id : null;
}
