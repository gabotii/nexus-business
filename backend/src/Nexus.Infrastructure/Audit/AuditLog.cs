using System.Text.Json;
using Nexus.Application.Abstractions;
using Nexus.Application.Tenancy;
using Nexus.Domain.Audit;
using Nexus.Infrastructure.Persistence;

namespace Nexus.Infrastructure.Audit;

/// <summary>Adds audit entries to the current unit of work (ADR-0009); nothing is written until SaveChanges.</summary>
internal sealed class AuditLog(
    AppDbContext db,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IRequestInfo request,
    TimeProvider clock) : IAuditLog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Tenant(string action, string entityType, Guid entityId, object? metadata = null)
    {
        if (tenant.TenantId == Guid.Empty)
        {
            throw new InvalidOperationException($"Audit action '{action}' requires a tenant context.");
        }

        var actor = currentUser.UserId == Guid.Empty ? (Guid?)null : currentUser.UserId;
        Add(tenant.TenantId, actor, actor is null ? AuditActorType.System : AuditActorType.User, action, entityType,
            entityId.ToString(), metadata);
    }

    public void Account(string action, Guid? userId, object? metadata = null) =>
        Add(null, userId, userId is null ? AuditActorType.Anonymous : AuditActorType.User, action, "user",
            userId?.ToString(), metadata);

    private void Add(Guid? tenantId, Guid? actor, AuditActorType actorType, string action, string? entityType,
        string? entityId, object? metadata) =>
        db.AuditLog.Add(new AuditEntry(Guid.CreateVersion7(), tenantId, actor, actorType, action, entityType, entityId,
            clock.GetUtcNow(), request.CorrelationId, request.IpPrefix,
            metadata is null ? null : JsonSerializer.Serialize(metadata, Json)));
}
