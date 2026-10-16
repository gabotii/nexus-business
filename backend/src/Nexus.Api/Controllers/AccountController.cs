using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Nexus.Api.Web;
using Nexus.Infrastructure.Identity;

namespace Nexus.Api.Controllers;

/// <summary>The signed-in user's own account: profile, password and devices.</summary>
[ApiController]
[Route("api/v1/account")]
public sealed class AccountController(
    AccountService accounts,
    SessionService sessions,
    SignInManager<AppUser> signInManager) : ControllerBase
{
    [HttpGet("me")]
    public async Task<MeDto> Me() => AccountService.ToMe(await accounts.GetUserAsync(User.UserId()));

    [HttpPut("profile")]
    public Task<MeDto> UpdateProfile(UpdateProfileRequest request, CancellationToken ct) =>
        accounts.UpdateProfileAsync(User.UserId(), request, ct);

    /// <summary>Changes the password. Every other session is signed out; this one stays signed in.</summary>
    [HttpPost("password")]
    public async Task<NoContentResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await accounts.ChangePasswordAsync(User.UserId(), User.SessionId(), request, ct);
        // The security stamp changed: re-issue this device's ticket so the stamp validator keeps accepting it.
        await signInManager.RefreshSignInAsync(user);
        return NoContent();
    }

    [HttpGet("sessions")]
    public Task<IReadOnlyList<SessionDto>> Sessions(CancellationToken ct) =>
        sessions.ListAsync(User.UserId(), User.SessionId(), ct);

    /// <summary>Signs out one device. Revoking the current session is the same as signing out.</summary>
    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<NoContentResult> RevokeSession(Guid sessionId, CancellationToken ct)
    {
        await sessions.RevokeAsync(User.UserId(), sessionId, ct);
        if (sessionId == User.SessionId())
        {
            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            Authentication.IssueAntiforgeryTokens(HttpContext, Authentication.Anonymous);
        }

        return NoContent();
    }

    /// <summary>Signs out every device except this one.</summary>
    [HttpPost("sessions/revoke-others")]
    public async Task<RevokedSessionsDto> RevokeOtherSessions(CancellationToken ct) =>
        new(await sessions.SignOutOtherDevicesAsync(User.UserId(), User.SessionId(), ct));
}

public sealed record RevokedSessionsDto(int Revoked);
