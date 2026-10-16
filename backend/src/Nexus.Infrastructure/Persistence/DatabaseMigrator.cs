using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Nexus.Infrastructure.Persistence;

/// <summary>Applies pending EF Core migrations with the schema-owner connection (ADR-0010).</summary>
public static class DatabaseMigrator
{
    public static async Task MigrateAsync(string ownerConnectionString, ILogger logger, CancellationToken ct = default)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseOptions.ConfigureProvider(builder, ownerConnectionString);
        await using var db = new AppDbContext(builder.Options);

        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("Database schema is up to date");
            return;
        }

        logger.LogInformation("Applying {Count} migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
        await db.Database.MigrateAsync(ct);
        logger.LogInformation("Migrations applied");
    }
}
