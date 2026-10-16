using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Application.Tenancy;
using Nexus.Domain.Tenancy;
using Nexus.Infrastructure.Persistence;
using Npgsql;

namespace Nexus.IntegrationTests;

/// <summary>Raw SQL as the API's runtime role, with explicit RLS session variables.</summary>
public static class Sql
{
    /// <summary>Runs <paramref name="sql"/> as <c>nexus_app</c> with app.tenant_id set (or empty) and returns the scalar.</summary>
    public static async Task<string?> AsTenantAsync(ApiFactory factory, Guid? tenantId, string sql, Guid? userId = null)
    {
        await using var connection = new NpgsqlConnection(factory.AppConnectionString);
        await connection.OpenAsync();
        await using (var set = new NpgsqlCommand(
                         "SELECT set_config('app.tenant_id', @t, false), set_config('app.user_id', @u, false)", connection))
        {
            set.Parameters.AddWithValue("t", tenantId?.ToString() ?? string.Empty);
            set.Parameters.AddWithValue("u", userId?.ToString() ?? string.Empty);
            await set.ExecuteNonQueryAsync();
        }

        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    public static async Task<string?> AsOwnerAsync(ApiFactory factory, string sql)
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// The database barriers of ADR-0004, tested below the application: Row-Level Security, composite foreign keys,
/// the EF write guard, append-only audit and the per-connection RLS context (R17).
/// </summary>
[Collection(SharedApi.Name)]
public sealed class IsolationTests(ApiFactory factory)
{
    private static readonly string[] TenantTables = ["tenants", "tenant_memberships", "roles", "role_permissions", "audit_log"];

    [Fact]
    public async Task Without_a_tenant_context_the_runtime_role_sees_no_company_rows()
    {
        using var owner = await factory.SignedInUserAsync();
        var tenantId = await owner.CreateCompanyAsync();

        foreach (var table in TenantTables)
        {
            Assert.Equal("0", await Sql.AsTenantAsync(factory, null,
                $"SELECT count(*) FROM {table} {(table == "audit_log" ? "WHERE tenant_id IS NOT NULL" : "")}"));
        }

        // The rows exist: inside the company's context they are visible.
        Assert.Equal("5", await Sql.AsTenantAsync(factory, tenantId, "SELECT count(*) FROM roles"));
    }

    [Fact]
    public async Task With_a_tenant_context_only_that_companys_rows_are_visible()
    {
        using var ownerA = await factory.SignedInUserAsync();
        var a = await ownerA.CreateCompanyAsync("Empresa A");
        using var ownerB = await factory.SignedInUserAsync();
        var b = await ownerB.CreateCompanyAsync("Empresa B");

        Assert.Equal("5", await Sql.AsTenantAsync(factory, a, "SELECT count(*) FROM roles"));
        Assert.Equal(a.ToString(), await Sql.AsTenantAsync(factory, a, "SELECT string_agg(DISTINCT tenant_id::text, ',') FROM roles"));
        Assert.Equal("1", await Sql.AsTenantAsync(factory, a, "SELECT count(*) FROM tenants"));
        Assert.Equal("0", await Sql.AsTenantAsync(factory, a, $"SELECT count(*) FROM role_permissions WHERE tenant_id = '{b}'"));
    }

    [Fact]
    public async Task A_user_context_reveals_only_that_users_memberships_and_companies()
    {
        using var owner = await factory.SignedInUserAsync();
        var mine = await owner.CreateCompanyAsync("Mía");
        using var other = await factory.SignedInUserAsync();
        await other.CreateCompanyAsync("Ajena");
        var userId = Guid.Parse((await Sql.AsOwnerAsync(factory, $"SELECT id FROM users WHERE email = '{owner.Email}'"))!);

        Assert.Equal(mine.ToString(), await Sql.AsTenantAsync(factory, null, "SELECT string_agg(id::text, ',') FROM tenants", userId));
        Assert.Equal("1", await Sql.AsTenantAsync(factory, null, "SELECT count(*) FROM tenant_memberships", userId));
        // Only the role the user holds, not the rest of the company's roles.
        Assert.Equal("1", await Sql.AsTenantAsync(factory, null, "SELECT count(*) FROM roles", userId));
    }

    [Fact]
    public async Task Row_level_security_rejects_writes_into_another_company()
    {
        using var ownerA = await factory.SignedInUserAsync();
        var a = await ownerA.CreateCompanyAsync("Empresa A");
        using var ownerB = await factory.SignedInUserAsync();
        var b = await ownerB.CreateCompanyAsync("Empresa B");

        var error = await Assert.ThrowsAsync<PostgresException>(() => Sql.AsTenantAsync(factory, a,
            $"INSERT INTO roles (id, tenant_id, name, created_at) VALUES (gen_random_uuid(), '{b}', 'Intruso', now())"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);

        // Updates and deletes of B's rows simply match nothing.
        Assert.Equal("0", await Sql.AsTenantAsync(factory, a,
            $"WITH u AS (UPDATE roles SET name = 'x' WHERE tenant_id = '{b}' RETURNING 1) SELECT count(*) FROM u"));
    }

    [Fact]
    public async Task Composite_foreign_keys_prevent_references_to_another_companys_rows()
    {
        using var ownerA = await factory.SignedInUserAsync();
        var a = await ownerA.CreateCompanyAsync("Empresa A");
        using var ownerB = await factory.SignedInUserAsync();
        var b = await ownerB.CreateCompanyAsync("Empresa B");
        var roleOfB = await ownerB.RoleIdAsync(b, SystemRoles.Employee);
        var userOfA = (await Sql.AsOwnerAsync(factory, $"SELECT id FROM users WHERE email = '{ownerA.Email}'"))!;

        var error = await Assert.ThrowsAsync<PostgresException>(() => Sql.AsTenantAsync(factory, a, $"""
            UPDATE tenant_memberships SET role_id = '{roleOfB}' WHERE tenant_id = '{a}' AND user_id = '{userOfA}'
            """));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Fact]
    public async Task The_audit_log_is_append_only_for_the_runtime_role()
    {
        using var owner = await factory.SignedInUserAsync();
        var a = await owner.CreateCompanyAsync();

        foreach (var sql in new[] { "UPDATE audit_log SET action = 'x'", "DELETE FROM audit_log", "TRUNCATE audit_log" })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => Sql.AsTenantAsync(factory, a, sql));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }
    }

    [Fact]
    public async Task A_pooled_connection_never_keeps_the_previous_tenant()
    {
        // One physical connection: every scope below reuses it.
        using var singleConnection = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = factory.AppConnectionString + ";Maximum Pool Size=1;Application Name=r17",
            })));
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var first = await ReadContextAsync(singleConnection.Services, tenantA);
        var second = await ReadContextAsync(singleConnection.Services, null);
        var third = await ReadContextAsync(singleConnection.Services, tenantB);

        Assert.Equal(first.Pid, second.Pid);
        Assert.Equal(first.Pid, third.Pid);
        Assert.Equal(tenantA.ToString(), first.Tenant);
        Assert.Equal(string.Empty, second.Tenant);
        Assert.Equal(tenantB.ToString(), third.Tenant);
    }

    [Fact]
    public async Task The_write_guard_refuses_changes_to_another_tenants_rows()
    {
        using var ownerA = await factory.SignedInUserAsync();
        var a = await ownerA.CreateCompanyAsync("Empresa A");
        using var ownerB = await factory.SignedInUserAsync();
        var b = await ownerB.CreateCompanyAsync("Empresa B");

        Role roleOfB;
        using (var scope = factory.Services.CreateScope())
        using (scope.ServiceProvider.GetRequiredService<TenantContext>().BeginSystemScope(b))
        {
            roleOfB = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Roles.AsNoTracking()
                .FirstAsync(r => r.SystemKey == SystemRoles.Employee);
        }

        using (var scope = factory.Services.CreateScope())
        using (scope.ServiceProvider.GetRequiredService<TenantContext>().BeginSystemScope(a))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Roles.Attach(roleOfB);
            roleOfB.Update("Hackeado", null, []);
            await Assert.ThrowsAsync<TenantIsolationException>(() => db.SaveChangesAsync());

            db.ChangeTracker.Clear();
            db.Roles.Add(Role.CreateCustom(b, "Intruso", null, [], DateTimeOffset.UtcNow));
            await Assert.ThrowsAsync<TenantIsolationException>(() => db.SaveChangesAsync());
        }

        using (var scope = factory.Services.CreateScope())
        {
            // No tenant at all: tenant-owned rows cannot even be created.
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Roles.Add(Role.CreateCustom(Guid.Empty, "Huérfano", null, [], DateTimeOffset.UtcNow));
            await Assert.ThrowsAsync<TenantIsolationException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task The_ef_filter_fails_closed_without_a_tenant()
    {
        using var owner = await factory.SignedInUserAsync();
        await owner.CreateCompanyAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(0, await db.Roles.CountAsync());
        Assert.Equal(0, await db.Memberships.CountAsync());
    }

    private static async Task<(int Pid, string Tenant)> ReadContextAsync(IServiceProvider services, Guid? tenantId)
    {
        using var scope = services.CreateScope();
        using var system = tenantId is { } id ? scope.ServiceProvider.GetRequiredService<TenantContext>().BeginSystemScope(id) : null;
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Database
            .SqlQueryRaw<string>("SELECT pg_backend_pid()::text || '|' || current_setting('app.tenant_id', true) AS \"Value\"")
            .SingleAsync();
        var parts = row.Split('|');
        return (int.Parse(parts[0], CultureInfo.InvariantCulture), parts[1]);
    }
}
