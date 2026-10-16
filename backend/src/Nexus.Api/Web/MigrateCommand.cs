using Nexus.Infrastructure.Persistence;

namespace Nexus.Api.Web;

/// <summary>
/// <c>dotnet Nexus.Api.dll migrate</c>: applies pending migrations with <c>ConnectionStrings:Migrations</c> (schema
/// owner) and exits. This is the only supported way to migrate outside development (ADR-0010).
/// </summary>
internal static class MigrateCommand
{
    public static async Task<int> RunAsync(WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Nexus.Migrate");
        var ownerConnection = app.Configuration.GetConnectionString("Migrations");
        if (string.IsNullOrWhiteSpace(ownerConnection))
        {
            logger.LogCritical("ConnectionStrings:Migrations is not configured; nothing was migrated");
            return 2;
        }

        try
        {
            await DatabaseMigrator.MigrateAsync(ownerConnection, logger);
            return 0;
        }
        catch (Exception ex) when (ex is Npgsql.NpgsqlException or InvalidOperationException)
        {
            logger.LogCritical(ex, "Migration failed; the database was left at the last successfully applied migration");
            return 1;
        }
    }
}
