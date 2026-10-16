using System.Diagnostics;

namespace Nexus.Api.Web;

/// <summary>
/// One structured log line per request (method, route, status, duration). Query strings and bodies are never
/// logged: they may contain personal data (customer searches, e-mails) or credentials. The correlation id returned
/// to clients is the W3C trace id, which the logging infrastructure already attaches to every entry as
/// <c>TraceId</c>: searching logs for an <c>X-Correlation-Id</c> finds the whole request.
/// </summary>
internal sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers[Correlation.HeaderName] = Correlation.Id(context);

        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            var status = context.Response.StatusCode;
            var level = context.Request.Path.StartsWithSegments("/health") ? LogLevel.Debug
                : status >= 500 ? LogLevel.Error
                : status >= 400 ? LogLevel.Warning
                : LogLevel.Information;

            // The route template ("api/v1/tenants/{tenantId}/customers/{id}") groups requests in log searches;
            // the concrete path keeps the identifiers needed to investigate one request.
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(no route)";
            logger.Log(level, "HTTP {Method} {Path} [{Route}] responded {StatusCode} in {ElapsedMs:0.0} ms",
                context.Request.Method, context.Request.Path.Value, route, status,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }
}

internal static class SecurityHeaders
{
    /// <summary>
    /// Defensive headers for every response. API responses also get a deny-all CSP (they are JSON, never documents)
    /// and <c>no-store</c>, because they contain tenant data that must not end up in shared or browser caches.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

        if (context.Request.Path.StartsWithSegments("/api"))
        {
            headers.CacheControl = "no-store";
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        }

        await next();
    });
}
