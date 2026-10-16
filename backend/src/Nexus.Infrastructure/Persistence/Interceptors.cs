using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Nexus.Application.Abstractions;
using Nexus.Application.Tenancy;
using Nexus.Domain.Common;
using Nexus.Domain.Tenancy;

namespace Nexus.Infrastructure.Persistence;

/// <summary>
/// Row-Level Security context (ADR-0004, R17): on <em>every</em> connection open (physical or from the pool) the
/// session variables <c>app.tenant_id</c> and <c>app.user_id</c> are overwritten with the current values, or with
/// an empty string when there is none. A value from a previous user of a pooled connection therefore never survives.
/// </summary>
internal sealed class RlsSessionInterceptor(ITenantContext tenant, ICurrentUser currentUser) : DbConnectionInterceptor
{
    internal const string Sql = "SELECT set_config('app.tenant_id', @tenant, false), set_config('app.user_id', @user, false)";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = CreateCommand(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = CreateCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CreateCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = Sql;
        AddParameter(command, "tenant", tenant.TenantId);
        AddParameter(command, "user", currentUser.UserId);
        return command;
    }

    private static void AddParameter(DbCommand command, string name, Guid value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value == Guid.Empty ? string.Empty : value.ToString();
        command.Parameters.Add(parameter);
    }
}

/// <summary>Raised when a write would cross the tenant boundary. Never expected: it indicates a bug or an attack.</summary>
public sealed class TenantIsolationException(string message) : InvalidOperationException(message);

/// <summary>
/// Second isolation barrier for writes (ADR-0004): new tenant-owned rows get the current tenant; changing or
/// deleting another tenant's rows, or moving a row to another tenant, is refused before any SQL is sent.
/// </summary>
internal sealed class TenantWriteGuard(ITenantContext tenant, ILogger<TenantWriteGuard> logger) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Check(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Check(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Check(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var current = tenant.TenantId;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            switch (entry.Entity)
            {
                case ITenantOwned owned:
                    CheckOwned(entry, owned, current);
                    break;
                case Tenant company when entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted:
                    if (company.Id != current)
                    {
                        Violation($"{entry.State} tenant {company.Id} outside its own context");
                    }

                    break;
            }
        }
    }

    private void CheckOwned(EntityEntry entry, ITenantOwned owned, Guid current)
    {
        var property = entry.Property(nameof(ITenantOwned.TenantId));
        switch (entry.State)
        {
            case EntityState.Added when owned.TenantId == Guid.Empty:
                if (current == Guid.Empty)
                {
                    Violation($"{entry.Metadata.ClrType.Name} added without a tenant context");
                }

                property.CurrentValue = current;
                break;
            case EntityState.Added when owned.TenantId != current:
                Violation($"{entry.Metadata.ClrType.Name} added for another tenant");
                break;
            case EntityState.Modified or EntityState.Deleted:
                if (property.IsModified || (Guid)property.OriginalValue! != current)
                {
                    Violation($"{entry.State} {entry.Metadata.ClrType.Name} of another tenant");
                }

                break;
        }
    }

    private void Violation(string what)
    {
        logger.LogCritical("Tenant isolation violation blocked: {What} (current tenant {TenantId})", what, tenant.TenantId);
        throw new TenantIsolationException($"Tenant isolation violation: {what}.");
    }
}
