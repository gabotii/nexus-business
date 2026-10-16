using Microsoft.EntityFrameworkCore;
using Nexus.Application.Abstractions;
using Npgsql;

namespace Nexus.Infrastructure.Persistence;

internal sealed class DatabaseTools(AppDbContext db) : IDatabaseTools
{
    public async Task LockTenantAsync(Guid tenantId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A tenant lock is only meaningful inside a transaction.");
        }

        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM tenants WHERE id = {tenantId} FOR UPDATE", ct);
    }

    public async Task LockAsync(string key, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("An advisory lock is only meaningful inside a transaction.");
        }

        // 64-bit hash of the key; released automatically at commit or rollback.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
    }

    public bool IsUniqueViolation(Exception exception, string constraint) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg &&
        pg.ConstraintName == constraint;
}
