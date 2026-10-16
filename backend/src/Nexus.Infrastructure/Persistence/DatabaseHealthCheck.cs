using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Nexus.Infrastructure.Persistence;

/// <summary>
/// Readiness: the database answers <em>and</em> the API is connected with a role that cannot bypass Row-Level
/// Security. Running as a superuser or a BYPASSRLS role would silently disable the second isolation barrier
/// (ADR-0004), so the instance reports itself unhealthy instead of serving traffic.
/// </summary>
internal sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var bypassesRls = await db.Database
                .SqlQueryRaw<bool>("select (rolsuper or rolbypassrls) as \"Value\" from pg_roles where rolname = current_user")
                .SingleAsync(ct);

            return bypassesRls
                ? HealthCheckResult.Unhealthy(
                    "The runtime database role can bypass Row-Level Security; connect as a role without SUPERUSER/BYPASSRLS.")
                : HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy("Database unreachable.", ex);
        }
    }
}
