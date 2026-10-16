using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Infrastructure.Persistence;
using Npgsql;

namespace Nexus.IntegrationTests;

/// <summary>
/// ADR-0010: the API's role can read and write data in tables the owner creates, but cannot change the schema and
/// cannot bypass Row-Level Security.
/// </summary>
[Collection(SharedApi.Name)]
public sealed class DatabasePrivilegeTests(ApiFactory factory) : IAsyncLifetime
{
    private readonly string _table = $"privilege_probe_{Guid.NewGuid():N}"[..40];

    public async Task InitializeAsync()
    {
        // Starting the host applies the migrations (default privileges) before the probe table is created.
        factory.CreateClient().Dispose();
        await ExecuteAsync(factory.OwnerConnectionString, $"CREATE TABLE {_table} (id int primary key, note text)");
    }

    public Task DisposeAsync() => ExecuteAsync(factory.OwnerConnectionString, $"DROP TABLE IF EXISTS {_table}");

    [Fact]
    public async Task Runtime_role_is_neither_superuser_nor_bypassrls()
    {
        await using var connection = new NpgsqlConnection(factory.AppConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select current_user, rolsuper, rolbypassrls from pg_roles where rolname = current_user", connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        Assert.Equal(DatabaseOptions.RuntimeRole, reader.GetString(0));
        Assert.False(reader.GetBoolean(1));
        Assert.False(reader.GetBoolean(2));
    }

    [Fact]
    public async Task Runtime_role_gets_dml_on_new_tables_through_default_privileges()
    {
        var app = factory.AppConnectionString;

        await ExecuteAsync(app, $"INSERT INTO {_table} (id, note) VALUES (1, 'a')");
        await ExecuteAsync(app, $"UPDATE {_table} SET note = 'b' WHERE id = 1");
        Assert.Equal(1L, await ScalarAsync(app, $"SELECT count(*) FROM {_table} WHERE note = 'b'"));
        await ExecuteAsync(app, $"DELETE FROM {_table} WHERE id = 1");
        Assert.Equal(0L, await ScalarAsync(app, $"SELECT count(*) FROM {_table}"));
    }

    [Theory]
    [InlineData("CREATE TABLE intruder (id int)")]
    [InlineData("DROP TABLE {table}")]
    [InlineData("ALTER TABLE {table} ADD COLUMN extra text")]
    [InlineData("TRUNCATE {table}")]
    public async Task Runtime_role_cannot_change_the_schema(string sql)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(factory.AppConnectionString, sql.Replace("{table}", _table, StringComparison.Ordinal)));

        // 42501 insufficient_privilege: the table exists; the role simply may not touch its definition.
        Assert.True(error.SqlState is PostgresErrorCodes.InsufficientPrivilege, error.SqlState + " " + error.MessageText);
    }

    [Fact]
    public async Task Migrating_an_up_to_date_database_is_a_no_op()
    {
        await DatabaseMigrator.MigrateAsync(factory.OwnerConnectionString, NullLogger.Instance);

        Assert.Equal(1L, await ScalarAsync(factory.OwnerConnectionString,
            "SELECT count(*) FROM __ef_migrations_history WHERE \"MigrationId\" LIKE '%_Baseline'"));
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
