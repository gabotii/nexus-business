using System.Security.Cryptography;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexus.Application.Abstractions;
using Nexus.Application.Common;
using Nexus.Domain.Common;
using Nexus.Domain.Tenancy;

namespace Nexus.Application.Tenancy;

/// <summary>Companies of the current user: listing, creation, the opened workspace and leaving.</summary>
public sealed class TenantService(
    IAppDbContext db,
    IDatabaseTools tools,
    ICurrentUser currentUser,
    TenantContext tenant,
    IUserDirectory users,
    IAuditLog audit,
    IValidator<CreateTenantRequest> createValidator,
    TimeProvider clock,
    ILogger<TenantService> logger)
{
    internal const string SlugConstraint = "ux_tenants_slug";

    /// <summary>
    /// Active memberships of the caller in active companies. This is the one read that spans tenants, so it ignores
    /// the tenant filter (allow-listed in ArchitectureTests); RLS still restricts it to the caller's own memberships.
    /// </summary>
    public async Task<IReadOnlyList<MyTenantDto>> ListMineAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        return await db.Memberships.IgnoreQueryFilters(["Tenant"])
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active)
            .Join(db.Tenants.Where(t => t.Status == TenantStatus.Active), m => m.TenantId, t => t.Id,
                (m, t) => new { m.RoleId, Tenant = t })
            .Join(db.Roles.IgnoreQueryFilters(["Tenant"]), x => x.RoleId, r => r.Id, (x, r) => new { x.Tenant, Role = r })
            .OrderBy(x => x.Tenant.Name)
            .Select(x => new MyTenantDto(x.Tenant.Id, x.Tenant.Name, x.Tenant.Slug,
                new RoleRef(x.Role.Id, x.Role.Name, x.Role.SystemKey)))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Creates a company with its five system roles and the caller as Owner, in one transaction. Requires a verified
    /// e-mail address. Runs in a system scope for the new tenant so every row passes the RLS write checks.
    /// </summary>
    public async Task<MyTenantDto> CreateAsync(CreateTenantRequest request, CancellationToken ct)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        var userId = currentUser.UserId;
        var user = await users.FindAsync(userId, ct) ?? throw new NotFoundException();
        if (!user.EmailConfirmed)
        {
            throw new ForbiddenException("email_not_verified",
                "Verifica tu correo electrónico antes de crear una empresa.");
        }

        var baseSlug = Tenant.SlugFrom(request.Name);
        for (var attempt = 0; ; attempt++)
        {
            var slug = attempt == 0
                ? baseSlug
                : $"{baseSlug}-{new string(RandomNumberGenerator.GetItems<char>("abcdefghijkmnpqrstuvwxyz23456789", 4))}";
            try
            {
                return await CreateWithSlugAsync(request, userId, slug, ct);
            }
            catch (DbUpdateException ex) when (tools.IsUniqueViolation(ex, SlugConstraint) && attempt < 5)
            {
                // Another company already uses the slug; RLS hides it, so the unique index is the only arbiter.
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<MyTenantDto> CreateWithSlugAsync(CreateTenantRequest request, Guid userId, string slug,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var company = new Tenant(Guid.CreateVersion7(), request.Name, slug, request.Currency ?? Currencies.Default,
            request.TimeZone ?? TimeZones.Default, userId, now);

        using var scope = tenant.BeginSystemScope(company.Id);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var roles = SystemRoles.Defaults.Select(d => Role.CreateSystem(company.Id, d, now)).ToList();
        var owner = roles.Single(r => r.IsOwner);
        var membership = new Membership(company.Id, userId, owner.Id, now);

        db.Tenants.Add(company);
        db.Roles.AddRange(roles);
        db.Memberships.Add(membership);
        audit.Tenant("tenant.created", "tenant", company.Id, new { company.Currency, company.TimeZone });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation("Tenant {TenantId} created by user {UserId}", company.Id, userId);
        return new MyTenantDto(company.Id, company.Name, company.Slug, new RoleRef(owner.Id, owner.Name, owner.SystemKey));
    }

    /// <summary>The company resolved for this request, with the caller's role and permissions.</summary>
    public async Task<WorkspaceDto> GetWorkspaceAsync(CancellationToken ct)
    {
        var company = await db.Tenants.SingleAsync(t => t.Id == tenant.TenantId, ct);
        var role = await db.Roles.SingleAsync(r => r.Id == tenant.RoleId, ct);
        return new WorkspaceDto(company.Id, company.Name, company.Slug, company.Currency, company.TimeZone,
            company.Locale, new RoleRef(role.Id, role.Name, role.SystemKey),
            [.. Permissions.All.Where(tenant.Permissions.Contains)], company.LogoFileId);
    }

    /// <summary>The caller leaves the company. The last active Owner cannot leave (the company would be orphaned).</summary>
    public async Task LeaveAsync(CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await tools.LockTenantAsync(tenant.TenantId, ct);

        var membership = await db.Memberships.SingleAsync(m => m.Id == tenant.MembershipId, ct);
        if (await TeamRules.IsOwnerAsync(db, membership.RoleId, ct) &&
            await TeamRules.ActiveOwnerCountAsync(db, ct) <= 1)
        {
            throw new DomainException("last_owner",
                "Eres el único propietario. Nombra a otro propietario antes de abandonar la empresa.");
        }

        membership.Disable(clock.GetUtcNow());
        audit.Tenant("members.left", "membership", membership.Id);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}

/// <summary>Queries shared by the team use cases; all run inside the current tenant.</summary>
internal static class TeamRules
{
    public static Task<bool> IsOwnerAsync(IAppDbContext db, Guid roleId, CancellationToken ct) =>
        db.Roles.AnyAsync(r => r.Id == roleId && r.SystemKey == SystemRoles.Owner, ct);

    public static Task<int> ActiveOwnerCountAsync(IAppDbContext db, CancellationToken ct) =>
        db.Memberships.CountAsync(m => m.Status == MembershipStatus.Active &&
                                       db.Roles.Any(r => r.Id == m.RoleId && r.SystemKey == SystemRoles.Owner), ct);
}
