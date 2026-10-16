using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Nexus.Application.Abstractions;
using Nexus.Application.Tenancy;
using Nexus.Domain.Audit;
using Nexus.Domain.Catalog;
using Nexus.Domain.Customers;
using Nexus.Domain.Files;
using Nexus.Domain.Common;
using Nexus.Domain.Tenancy;
using Nexus.Infrastructure.Email;
using Nexus.Infrastructure.Identity;

namespace Nexus.Infrastructure.Persistence;

/// <summary>
/// The single unit of work for every module (ADR-0001). Entity configurations live next to this class in
/// <c>Configurations/</c>; table and column names are converted to snake_case after they are applied.
/// Every <see cref="ITenantOwned"/> entity gets the named query filter <see cref="TenantFilter"/>, which compares
/// with the current tenant (or <see cref="Guid.Empty"/> without one, so it fails closed).
/// </summary>
public sealed class AppDbContext : IdentityUserContext<AppUser, Guid>, IAppDbContext
{
    public const string TenantFilter = "Tenant";

    private readonly ITenantContext? _tenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext? tenant = null) : base(options) =>
        _tenant = tenant;

    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<EmailMessage> EmailOutbox => Set<EmailMessage>();

    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<CustomerTag> CustomerTags => Set<CustomerTag>();

    public DbSet<CustomerNote> CustomerNotes => Set<CustomerNote>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();

    /// <summary>Read by the query filters at query time (EF parameterises it per context instance).</summary>
    private Guid CurrentTenantId => _tenant?.TenantId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // Trigram indexes for substring search (a "trusted" extension: the database owner may create it).
        builder.HasPostgresExtension("pg_trgm");
        IdentityModel.Configure(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entity in builder.Model.GetEntityTypes()
                     .Where(e => typeof(ITenantOwned).IsAssignableFrom(e.ClrType)))
        {
            ApplyTenantFilterMethod.MakeGenericMethod(entity.ClrType).Invoke(this, [builder]);
        }

        SnakeCaseNaming.Apply(builder);
    }

    private static readonly System.Reflection.MethodInfo ApplyTenantFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(ApplyTenantFilter),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

    private void ApplyTenantFilter<TEntity>(ModelBuilder builder) where TEntity : class, ITenantOwned
    {
        Expression<Func<TEntity, bool>> filter = e => e.TenantId == CurrentTenantId;
        builder.Entity<TEntity>().HasQueryFilter(TenantFilter, filter);
    }
}

/// <summary>Identity's tables, renamed to the project's conventions. Roles are per company and not Identity's.</summary>
internal static class IdentityModel
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<AppUser>(b =>
        {
            b.ToTable("users");
            b.Property(u => u.DisplayName).HasMaxLength(AppUser.DisplayNameMaxLength).IsRequired();
            // Identity enforces unique e-mails in code; the index makes it hold under concurrent registrations.
            b.HasIndex(u => u.NormalizedEmail).HasDatabaseName("ux_users_normalized_email").IsUnique();
        });
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
    }
}
