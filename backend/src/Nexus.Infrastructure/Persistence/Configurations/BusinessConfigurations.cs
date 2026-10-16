using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.Domain.Catalog;
using Nexus.Domain.Common;
using Nexus.Domain.Customers;
using Nexus.Domain.Files;
using Nexus.Domain.Tenancy;
using Nexus.Infrastructure.Identity;

namespace Nexus.Infrastructure.Persistence.Configurations;

/// <summary>Column sizes shared by the address of customers and of the company itself.</summary>
internal static class AddressColumns
{
    public static void Configure<T>(EntityTypeBuilder<T> b) where T : class
    {
        b.Property<string?>("AddressLine").HasMaxLength(PostalAddress.LineMaxLength);
        b.Property<string?>("City").HasMaxLength(PostalAddress.CityMaxLength);
        b.Property<string?>("PostalCode").HasMaxLength(PostalAddress.PostalCodeMaxLength);
        b.Property<string?>("Region").HasMaxLength(PostalAddress.RegionMaxLength);
        b.Property<string?>("CountryCode").HasMaxLength(2).IsFixedLength();
    }
}

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("customers", t => t.HasCheckConstraint("ck_customers_kind", "kind IN ('Person', 'Company')"));
        b.Property(c => c.Id).ValueGeneratedNever();
        b.HasAlternateKey(c => new { c.TenantId, c.Id });
        b.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.Property(c => c.Kind).HasConversion<string>().HasMaxLength(16);
        // Explicit ICU collation: the server default differs per machine (byte order on Linux CI), so listings sorted
        // by name would change order between environments.
        b.Property(c => c.Name).HasMaxLength(Customer.NameMaxLength).IsRequired().UseCollation("und-x-icu");
        b.Property(c => c.LegalName).HasMaxLength(Customer.LegalNameMaxLength);
        b.Property(c => c.TaxId).HasMaxLength(Customer.TaxIdMaxLength);
        b.Property(c => c.NormalizedTaxId).HasMaxLength(Customer.TaxIdMaxLength);
        b.Property(c => c.Email).HasMaxLength(Customer.EmailMaxLength);
        b.Property(c => c.NormalizedEmail).HasMaxLength(Customer.EmailMaxLength);
        b.Property(c => c.Phone).HasMaxLength(Customer.PhoneMaxLength);
        AddressColumns.Configure(b);
        b.Property(c => c.SearchText).IsRequired();
        b.Property(c => c.Version).IsRowVersion();
        b.Ignore(c => c.IsArchived);
        b.Ignore(c => c.Address);

        // Duplicate checks and listings.
        b.HasIndex(c => new { c.TenantId, c.NormalizedEmail });
        b.HasIndex(c => new { c.TenantId, c.NormalizedTaxId });
        b.HasIndex(c => new { c.TenantId, c.Name });
        // Substring search (LIKE '%term%') on the normalised text: trigram index.
        b.HasIndex(c => c.SearchText).HasMethod("gin").HasOperators("gin_trgm_ops")
            .HasDatabaseName("ix_customers_search_text_trgm");
        b.HasOne<AppUser>().WithMany().HasForeignKey(c => c.CreatedBy).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(c => c.Tags).WithOne()
            .HasForeignKey(t => new { t.TenantId, t.CustomerId })
            .HasPrincipalKey(c => new { c.TenantId, c.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(c => c.Tags).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class CustomerTagConfiguration : IEntityTypeConfiguration<CustomerTag>
{
    public void Configure(EntityTypeBuilder<CustomerTag> b)
    {
        b.ToTable("customer_tags");
        b.HasKey(t => new { t.CustomerId, t.TagId });
        b.HasOne<Tag>().WithMany()
            .HasForeignKey(t => new { t.TenantId, t.TagId })
            .HasPrincipalKey(t => new { t.TenantId, t.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CustomerNoteConfiguration : IEntityTypeConfiguration<CustomerNote>
{
    public void Configure(EntityTypeBuilder<CustomerNote> b)
    {
        b.ToTable("customer_notes");
        b.Property(n => n.Id).ValueGeneratedNever();
        b.Property(n => n.Body).HasMaxLength(CustomerNote.BodyMaxLength).IsRequired();
        b.HasOne<Customer>().WithMany()
            .HasForeignKey(n => new { n.TenantId, n.CustomerId })
            .HasPrincipalKey(c => new { c.TenantId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(n => n.AuthorId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(n => new { n.TenantId, n.CustomerId, n.CreatedAt });
    }
}

internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> b)
    {
        b.ToTable("tags");
        b.Property(t => t.Id).ValueGeneratedNever();
        b.HasAlternateKey(t => new { t.TenantId, t.Id });
        b.HasOne<Tenant>().WithMany().HasForeignKey(t => t.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.Property(t => t.Name).HasMaxLength(Tag.NameMaxLength).IsRequired();
        b.Property(t => t.NormalizedName).HasMaxLength(Tag.NameMaxLength).IsRequired();
        b.Property(t => t.Color).HasMaxLength(16).IsRequired();
        b.HasIndex(t => new { t.TenantId, t.NormalizedName }).IsUnique().HasDatabaseName("ux_tags_tenant_name");
    }
}

internal sealed class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> b)
    {
        b.ToTable("product_categories");
        b.Property(c => c.Id).ValueGeneratedNever();
        b.HasAlternateKey(c => new { c.TenantId, c.Id });
        b.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.Property(c => c.Name).HasMaxLength(ProductCategory.NameMaxLength).IsRequired();
        b.Property(c => c.NormalizedName).HasMaxLength(ProductCategory.NameMaxLength).IsRequired();
        b.HasIndex(c => new { c.TenantId, c.NormalizedName }).IsUnique().HasDatabaseName("ux_product_categories_tenant_name");
        b.Ignore(c => c.IsArchived);
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("products", t =>
        {
            t.HasCheckConstraint("ck_products_kind", "kind IN ('Product', 'Service')");
            t.HasCheckConstraint("ck_products_amounts", "unit_price >= 0 AND (cost IS NULL OR cost >= 0) AND min_stock >= 0");
            t.HasCheckConstraint("ck_products_tax_rate", "tax_rate >= 0 AND tax_rate <= 100");
            t.HasCheckConstraint("ck_products_service_stock", "kind <> 'Service' OR NOT track_inventory");
        });
        b.Property(p => p.Id).ValueGeneratedNever();
        b.HasAlternateKey(p => new { p.TenantId, p.Id });
        b.HasOne<Tenant>().WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.Property(p => p.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(p => p.Sku).HasMaxLength(Product.SkuMaxLength).IsRequired();
        b.Property(p => p.NormalizedSku).HasMaxLength(Product.SkuMaxLength).IsRequired();
        b.HasIndex(p => new { p.TenantId, p.NormalizedSku }).IsUnique().HasDatabaseName("ux_products_tenant_sku");
        b.Property(p => p.Name).HasMaxLength(Product.NameMaxLength).IsRequired().UseCollation("und-x-icu"); // see Customer.Name
        b.Property(p => p.Description).HasMaxLength(Product.DescriptionMaxLength);
        b.Property(p => p.Unit).HasMaxLength(16).IsRequired();
        b.Property(p => p.UnitPrice).HasPrecision(19, Amounts.MoneyScale);
        b.Property(p => p.Cost).HasPrecision(19, Amounts.MoneyScale);
        b.Property(p => p.TaxRate).HasPrecision(5, Amounts.RateScale);
        b.Property(p => p.MinStock).HasPrecision(18, Amounts.QuantityScale);
        b.Property(p => p.SearchText).IsRequired();
        b.HasIndex(p => p.SearchText).HasMethod("gin").HasOperators("gin_trgm_ops")
            .HasDatabaseName("ix_products_search_text_trgm");
        b.HasIndex(p => new { p.TenantId, p.Name });
        b.Property(p => p.Version).IsRowVersion();
        b.Ignore(p => p.IsArchived);
        // A product can only reference a category of its own company.
        b.HasOne<ProductCategory>().WithMany()
            .HasForeignKey(p => new { p.TenantId, p.CategoryId })
            .HasPrincipalKey(c => new { c.TenantId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(p => p.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> b)
    {
        b.ToTable("stored_files", t => t.HasCheckConstraint("ck_stored_files_size", "size_bytes > 0"));
        b.Property(f => f.Id).ValueGeneratedNever();
        b.HasOne<Tenant>().WithMany().HasForeignKey(f => f.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.Property(f => f.Purpose).HasConversion<string>().HasMaxLength(32);
        b.Property(f => f.ContentType).HasMaxLength(64).IsRequired();
        b.Property(f => f.Sha256).HasMaxLength(32).IsRequired();
        b.Property(f => f.StorageKey).HasMaxLength(128).IsRequired();
        b.HasIndex(f => f.StorageKey).IsUnique();
        b.HasOne<AppUser>().WithMany().HasForeignKey(f => f.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
