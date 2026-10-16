using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Nexus.Api.Web;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Requests per minute and client IP for the whole API (abuse, scrapers, runaway clients).</summary>
    [Range(1, 1_000_000)]
    public int PerIpPerMinute { get; set; } = 600;

    /// <summary>
    /// Requests per minute and client IP to the credential endpoints (sign-in, registration, password recovery,
    /// e-mail verification): slows down password guessing and account enumeration on top of Identity's lockout.
    /// </summary>
    [Range(1, 1_000_000)]
    public int CredentialsPerIpPerMinute { get; set; } = 10;
}

/// <summary>
/// In-memory, per-instance limits (ADR-0001: enough for a single replica): a global budget per client IP and a
/// stricter <see cref="CredentialsPolicy"/> for the endpoints that accept passwords or e-mailed tokens.
/// </summary>
internal static class RateLimiting
{
    public const string CredentialsPolicy = "credentials";

    public static IServiceCollection AddNexusRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
                }

                var problems = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problems.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Detail = "Has realizado demasiadas solicitudes. Espera un momento e inténtalo de nuevo.",
                        Extensions = { ["code"] = "rate_limited" },
                    },
                });
            };

            options.AddPolicy(CredentialsPolicy, context =>
            {
                var limit = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value
                    .CredentialsPerIpPerMinute;
                return RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1) });
            });

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var limit = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value.PerIpPerMinute;
                // Behind the trusted proxy, ForwardedHeaders has already replaced RemoteIpAddress with the client's.
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit,
                    Window = TimeSpan.FromMinutes(1),
                });
            });
        });
    }
}
