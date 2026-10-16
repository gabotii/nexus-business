using Microsoft.EntityFrameworkCore;

namespace Nexus.Infrastructure.Persistence;

/// <summary>
/// Database settings (ADR-0010). The API runs as the unprivileged runtime role; only the <c>migrate</c> command (and
/// <c>MigrateOnStartup</c> in development) uses the owner connection.
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>Runtime role: DML only, no BYPASSRLS. Migrations grant it privileges by this fixed name.</summary>
    public const string RuntimeRole = "nexus_app";

    /// <summary><c>ConnectionStrings:Default</c>, authenticating as <see cref="RuntimeRole"/>.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary><c>ConnectionStrings:Migrations</c>, authenticating as the schema owner. Not needed by the API itself.</summary>
    public string? MigrationsConnectionString { get; set; }

    /// <summary><c>Database:MigrateOnStartup</c>. Development convenience only; rejected in Production.</summary>
    public bool MigrateOnStartup { get; set; }

    internal const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>The provider configuration shared by the API, the migrate command and the EF design-time tools.</summary>
    internal static void ConfigureProvider(DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable(MigrationsHistoryTable);
            npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name);
        });
}
