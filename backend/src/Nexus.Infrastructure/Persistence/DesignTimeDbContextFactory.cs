using Microsoft.EntityFrameworkCore.Design;

namespace Nexus.Infrastructure.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c> only. Creating or listing migrations does not need a live database; commands that do
/// (rare: the API's <c>migrate</c> command is the supported path) read NEXUS_MIGRATIONS_CONNECTION.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NEXUS_MIGRATIONS_CONNECTION")
                               ?? "Host=localhost;Database=nexus_design_time";
        var builder = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<AppDbContext>();
        DatabaseOptions.ConfigureProvider(builder, connectionString);
        return new AppDbContext(builder.Options);
    }
}
