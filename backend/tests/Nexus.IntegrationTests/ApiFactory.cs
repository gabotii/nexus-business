using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Nexus.IntegrationTests;

/// <summary>
/// Hosts the real API against a throw-away PostgreSQL database, created per test run and dropped afterwards.
/// <list type="bullet">
/// <item>NEXUS_TEST_DB_OWNER: connection string of a role with CREATEDB; it owns the test database and runs the
/// migrations (local default: the <c>nexus</c> development role).</item>
/// <item>NEXUS_TEST_DB_APP_PASSWORD: password of the existing <c>nexus_app</c> runtime role, used by the API.</item>
/// </list>
/// The API therefore runs exactly as in production: migrated by the owner, serving traffic as the RLS-bound role.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string ProductName = "Nexus Test Suite";
    public const string PublicBaseUrl = "https://app.test";

    private readonly string _ownerServer = Environment.GetEnvironmentVariable("NEXUS_TEST_DB_OWNER")
                                           ?? "Host=127.0.0.1;Port=5432;Username=nexus;Password=nexus_dev;Database=postgres";

    private readonly string _appPassword = Environment.GetEnvironmentVariable("NEXUS_TEST_DB_APP_PASSWORD") ?? "nexus_app_dev";

    public string DatabaseName { get; } = $"nexus_test_{Guid.NewGuid():N}";

    /// <summary>Uploaded files of this run (deleted afterwards).</summary>
    public string StoragePath => Path.Combine(Path.GetTempPath(), "nexus-tests", DatabaseName);

    public string OwnerConnectionString => new NpgsqlConnectionStringBuilder(_ownerServer) { Database = DatabaseName }
        .ConnectionString;

    public string AppConnectionString => new NpgsqlConnectionStringBuilder(_ownerServer)
    {
        Database = DatabaseName,
        Username = "nexus_app",
        Password = _appPassword,
    }.ConnectionString;

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(_ownerServer);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{DatabaseName}\"", connection);
        await command.ExecuteNonQueryAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        // Pooled idle connections (of both roles) would block DROP DATABASE.
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(_ownerServer);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
        if (Directory.Exists(StoragePath))
        {
            Directory.Delete(StoragePath, recursive: true);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = AppConnectionString,
            ["ConnectionStrings:Migrations"] = OwnerConnectionString,
            ["Database:MigrateOnStartup"] = "true",
            ["Branding:ProductName"] = ProductName,
            ["RateLimiting:PerIpPerMinute"] = "100000",
            ["RateLimiting:CredentialsPerIpPerMinute"] = "100000",
            ["App:PublicBaseUrl"] = PublicBaseUrl,
            // Tests read the outbox table directly; nothing is delivered.
            ["Email:Delivery"] = "Pickup",
            ["Email:DispatcherEnabled"] = "false",
            ["Storage:RootPath"] = StoragePath,
        }));
        builder.ConfigureTestServices(services =>
            services.AddMvcCore().AddApplicationPart(typeof(ApiFactory).Assembly));
    }
}

[CollectionDefinition(Name)]
public sealed class SharedApi : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
