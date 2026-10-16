using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nexus.Api.Web;
using Nexus.Infrastructure.Identity;

namespace Nexus.Api.Controllers;

/// <summary>
/// Registration, e-mail verification, sign-in/out and password recovery. Answers never reveal whether an e-mail
/// address has an account (R4).
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    AccountService accounts,
    SignInManager<AppUser> signInManager,
    TimeProvider clock) : ControllerBase
{
    /// <summary>Issues the antiforgery cookie pair. The SPA calls it on start-up and when a token is rejected.</summary>
    [HttpGet("antiforgery")]
    [AllowAnonymous]
    public NoContentResult Antiforgery()
    {
        Authentication.IssueAntiforgeryTokens(HttpContext, User);
        return NoContent();
    }

    /// <summary>Creates an account and e-mails a verification link. Same answer when the address is already registered.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.CredentialsPolicy)]
    public async Task<AcceptedResult> Register(RegisterRequest request, CancellationToken ct)
    {
        await accounts.RegisterAsync(request, ct);
        return Accepted();
    }

    [HttpPost("verify-email")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.CredentialsPolicy)]
    public async Task<NoContentResult> VerifyEmail(VerifyEmailRequest request, CancellationToken ct)
    {
        await accounts.VerifyEmailAsync(request, ct);
        return NoContent();
    }

    /// <summary>Sends the verification link again to the signed-in user (no-op when already verified).</summary>
    [HttpPost("resend-verification")]
    [EnableRateLimiting(RateLimiting.CredentialsPolicy)]
    public async Task<AcceptedResult> ResendVerification(CancellationToken ct)
    {
        await accounts.ResendVerificationAsync(User.UserId(), ct);
        return Accepted();
    }

    /// <summary>
    /// Signs in and starts a new server-side session. A session cookie sent with this request is revoked first
    /// (session fixation), even when the credentials turn out to be wrong.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [StartsNewSession]
    [EnableRateLimiting(RateLimiting.CredentialsPolicy)]
    public async Task<MeDto> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await accounts.AuthenticateAsync(request, ct);
        await signInManager.SignInAsync(user, Authentication.SessionProperties(request.RememberMe, clock.GetUtcNow()));
        Authentication.IssueAntiforgeryTokens(HttpContext, await signInManager.CreateUserPrincipalAsync(user));
        return AccountService.ToMe(user);
    }

    /// <summary>Ends the current session (revoked server-side). Safe to call without a session.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<NoContentResult> Logout()
    {
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        Authentication.IssueAntiforgeryTokens(HttpContext, Authentication.Anonymous);
        return NoContent();
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.CredentialsPolicy)]
    public async Task<AcceptedResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        await accounts.ForgotPasswordAsync(request, ct);
        return Accepted();
    }

    /// <summary>Sets a new password from an e-mailed link; every session of the account is signed out.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.CredentialsPolicy)]
    public async Task<NoContentResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        await accounts.ResetPasswordAsync(request, ct);
        return NoContent();
    }
}
