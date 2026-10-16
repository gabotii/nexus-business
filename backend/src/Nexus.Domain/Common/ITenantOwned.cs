namespace Nexus.Domain.Common;

/// <summary>
/// Marks business data that belongs to exactly one company (ADR-0004). Every such entity gets the tenant query
/// filter, the write guard and Row-Level Security; <see cref="TenantId"/> is assigned from the request context, never
/// from client input.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}
