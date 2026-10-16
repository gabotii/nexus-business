using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.Domain.Tenancy;
using Nexus.Infrastructure.Identity;

namespace Nexus.Infrastructure.Persistence.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("tenants", t =>
        {
            t.HasCheckConstraint("ck_tenants_slug", "slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
            t.HasCheckConstraint("ck_tenants_status", "status IN ('Active', 'Suspended')");
            t.HasCheckConstraint("ck_tenants_customer_duplicate_rule",
                "customer_duplicate_rule IN ('None', 'Email', 'TaxId', 'EmailOrTaxId')");
        });
        b.Property(t => t.Id).ValueGeneratedNever();
        b.Property(t => t.Name).HasMaxLength(Tenant.NameMaxLength).IsRequired();
        b.Property(t => t.Slug).HasMaxLength(Tenant.SlugMaxLength).IsRequired();
        b.HasIndex(t => t.Slug).IsUnique().HasDatabaseName("ux_tenants_slug");
        b.Property(t => t.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(t => t.TimeZone).HasMaxLength(64).IsRequired();
        b.Property(t => t.Locale).HasMaxLength(16).IsRequired();
        b.Property(t => t.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(t => t.Version).IsRowVersion();
        b.HasOne<AppUser>().WithMany().HasForeignKey(t => t.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        b.Property(t => t.LegalName).HasMaxLength(200);
        b.Property(t => t.TaxId).HasMaxLength(32);
        b.Property(t => t.Email).HasMaxLength(254);
        b.Property(t => t.Phone).HasMaxLength(40);
        AddressColumns.Configure(b);
        b.Ignore(t => t.Address);
        b.Property(t => t.CustomerDuplicateRule).HasConversion<string>().HasMaxLength(16);
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("roles", t => t.HasCheckConstraint("ck_roles_system_key",
            "system_key IS NULL OR system_key IN ('owner', 'administrator', 'manager', 'employee', 'read_only')"));
        b.Property(r => r.Id).ValueGeneratedNever();
        // Target of the composite foreign keys (tenant_id, role_id): a reference can never cross companies.
        b.HasAlternateKey(r => new { r.TenantId, r.Id });
        b.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.Property(r => r.Name).HasMaxLength(Role.NameMaxLength).IsRequired();
        b.Property(r => r.Description).HasMaxLength(Role.DescriptionMaxLength);
        b.Property(r => r.SystemKey).HasMaxLength(32);
        b.HasIndex(r => new { r.TenantId, r.SystemKey }).IsUnique()
            .HasFilter("system_key IS NOT NULL").HasDatabaseName("ux_roles_tenant_system_key");
        // Case-insensitive unique names: ux_roles_tenant_name on (tenant_id, lower(name)), created in the migration.
        b.Property(r => r.Version).IsRowVersion();
        b.Ignore(r => r.EffectivePermissions);

        b.HasMany(r => r.Permissions).WithOne()
            .HasForeignKey(p => new { p.TenantId, p.RoleId })
            .HasPrincipalKey(r => new { r.TenantId, r.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(r => r.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.ToTable("role_permissions");
        b.HasKey(p => new { p.RoleId, p.Permission });
        b.Property(p => p.Permission).HasMaxLength(64);
    }
}

internal sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> b)
    {
        b.ToTable("tenant_memberships", t =>
            t.HasCheckConstraint("ck_tenant_memberships_status", "status IN ('Active', 'Disabled')"));
        b.Property(m => m.Id).ValueGeneratedNever();
        b.HasIndex(m => new { m.TenantId, m.UserId }).IsUnique().HasDatabaseName("ux_tenant_memberships_tenant_user");
        b.HasIndex(m => m.UserId);
        b.HasOne<Tenant>().WithMany().HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Role>().WithMany()
            .HasForeignKey(m => new { m.TenantId, m.RoleId })
            .HasPrincipalKey(r => new { r.TenantId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.Property(m => m.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(m => m.Version).IsRowVersion();
        b.Ignore(m => m.IsActive);
    }
}

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> b)
    {
        b.ToTable("invitations");
        b.Property(i => i.Id).ValueGeneratedNever();
        b.Property(i => i.Email).HasMaxLength(254).IsRequired();
        b.Property(i => i.NormalizedEmail).HasMaxLength(254).IsRequired();
        b.Property(i => i.TokenHash).HasMaxLength(32).IsRequired();
        b.HasIndex(i => i.TokenHash).IsUnique().HasDatabaseName("ux_invitations_token_hash");
        b.HasIndex(i => new { i.TenantId, i.NormalizedEmail }).IsUnique()
            .HasFilter("accepted_at IS NULL AND revoked_at IS NULL").HasDatabaseName("ux_invitations_open_email");
        b.HasOne<Tenant>().WithMany().HasForeignKey(i => i.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Role>().WithMany()
            .HasForeignKey(i => new { i.TenantId, i.RoleId })
            .HasPrincipalKey(r => new { r.TenantId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(i => i.InvitedBy).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(i => i.AcceptedBy).OnDelete(DeleteBehavior.Restrict);
        b.Ignore(i => i.IsOpen);
    }
}
