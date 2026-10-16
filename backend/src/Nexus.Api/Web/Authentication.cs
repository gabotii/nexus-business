using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Nexus.Api.Web;

/// <summary>
/// Session cookie, server-side sessions and CSRF protection (ADR-0002). The API never redirects: an anonymous call to
/// a protected endpoint answers 401 and a forbidden one 403, both as problem details.
/// </summary>
internal static class Authentication
{
    public const string SessionCookie = "nexus.session";
    public const string AntiforgeryCookie = "nexus.af";

    /// <summary>Readable by the SPA, which echoes it in <see cref="XsrfHeader"/> (double submit).</summary>
    public const string XsrfCookie = "XSRF-TOKEN";

    public const string XsrfHeader = "X-XSRF-TOKEN";

    public static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(8);
    public static readonly TimeSpan RememberMeLifetime = TimeSpan.FromDays(14);

    public static IServiceCollection AddNexusAuthentication(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme, options =>
            {
                options.Cookie.Name = SessionCookie;
                options.Cookie.HttpOnly = true;
                // Browsers treat http://localhost as a secure context, so this also works in local development.
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.Path = "/";
                options.ExpireTimeSpan = IdleTimeout;
                options.SlidingExpiration = true;
                options.Events = new CookieAuthenticationEvents
                {
                    // Password changes on another device rotate the security stamp; this signs out stale tickets.
                    OnValidatePrincipal = SecurityStampValidator.ValidatePrincipalAsync,
                    OnRedirectToLogin = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    },
                };
            });
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<ITicketStore>((options, store) => options.SessionStore = store);

        // Every endpoint requires a signed-in user unless it opts out with [AllowAnonymous].
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddAntiforgery(options =>
        {
            options.HeaderName = XsrfHeader;
            options.Cookie.Name = AntiforgeryCookie;
            options.Cookie.HttpOnly = true;
            // Not a credential (it is useless without the session cookie): it follows the request scheme, which
            // behind the TLS proxy is HTTPS thanks to X-Forwarded-Proto. "Always" would make antiforgery throw on
            // plain-HTTP local runs.
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
            // Our own X-Frame-Options: DENY is stricter than the SAMEORIGIN antiforgery would add.
            options.SuppressXFrameOptionsHeader = true;
        });

        return services;
    }

    /// <summary>
    /// Rejects state-changing API requests without a valid antiforgery token (code <c>csrf_failed</c>; the SPA
    /// fetches a new token and retries once). Runs after authorization, so an expired session answers 401 first.
    /// </summary>
    public static IApplicationBuilder UseAntiforgeryProtection(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/api") && !IsSafe(context.Request.Method))
        {
            var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context,
                    ProblemDetails = GlobalExceptionHandler.Problem(StatusCodes.Status400BadRequest, "csrf_failed",
                        "La sesión del formulario ha caducado. Vuelve a intentarlo."),
                });
                return;
            }
        }

        await next();
    });

    /// <summary>
    /// Issues a fresh token pair for <paramref name="user"/> (the token is bound to the identity, so it must be
    /// reissued whenever the caller signs in or out).
    /// </summary>
    public static void IssueAntiforgeryTokens(HttpContext context, ClaimsPrincipal user)
    {
        context.User = user;
        var tokens = context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
        context.Response.Cookies.Append(XsrfCookie, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });
    }

    public static ClaimsPrincipal Anonymous => new(new ClaimsIdentity());

    private static bool IsSafe(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) ||
        HttpMethods.IsTrace(method);

    /// <summary>Properties for a new session: 8 h sliding by default, 14 days with "remember me".</summary>
    public static AuthenticationProperties SessionProperties(bool rememberMe, DateTimeOffset now) => new()
    {
        IsPersistent = rememberMe,
        ExpiresUtc = rememberMe ? now + RememberMeLifetime : null,
        AllowRefresh = true,
    };
}
