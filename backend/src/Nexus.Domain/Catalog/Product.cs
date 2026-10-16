using Nexus.Domain.Common;

namespace Nexus.Domain.Catalog;

public enum ProductKind
{
    Product,
    Service,
}

/// <summary>Units a product is sold in. Codes are stored; the client shows the labels.</summary>
public static class Units
{
    public static readonly IReadOnlyList<string> All = ["unit", "kg", "g", "l", "ml", "m", "m2", "hour", "day", "box"];

    public static bool IsKnown(string? code) => code is not null && All.Contains(code);
}

/// <summary>A flat product category (v1 has no hierarchy). Archived categories cannot receive new products.</summary>
public sealed class ProductCategory : ITenantOwned
{
    public const int NameMaxLength = 60;

    private ProductCategory()
    {
        Name = NormalizedName = string.Empty;
    }

    public ProductCategory(Guid tenantId, string name, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        CreatedAt = now;
        Name = NormalizedName = string.Empty;
        Rename(name);
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; }

    public string NormalizedName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    public void Rename(string name)
    {
        Name = name.Trim();
        NormalizedName = TextNormalization.ForSearch(name);
    }

    public void Archive(DateTimeOffset now) => ArchivedAt ??= now;

    public void Restore() => ArchivedAt = null;
}

/// <summary>The editable data of a product, already validated by the use case (amounts within their scale).</summary>
public sealed record ProductDetails(
    ProductKind Kind,
    string Sku,
    string Name,
    string? Description,
    Guid? CategoryId,
    string Unit,
    decimal UnitPrice,
    decimal TaxRate,
    bool TrackInventory,
    decimal MinStock);

/// <summary>
/// Something the company sells. Prices are net (without tax) in the company currency (ADR-0006, product decision
/// §9). Services never track stock. The cost is a separate field because only some roles may see it.
/// </summary>
public sealed class Product : ITenantOwned
{
    public const int SkuMaxLength = 40;
    public const int NameMaxLength = 150;
    public const int DescriptionMaxLength = 2000;

    private Product()
    {
        Sku = NormalizedSku = Name = Unit = SearchText = string.Empty;
    }

    public Product(Guid tenantId, ProductDetails details, decimal? cost, Guid createdBy, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        CreatedAt = now;
        CreatedBy = createdBy;
        Sku = NormalizedSku = Name = Unit = SearchText = string.Empty;
        Apply(details);
        SetCost(cost);
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public ProductKind Kind { get; private set; }

    public string Sku { get; private set; }

    /// <summary>Upper-case SKU: unique per company regardless of case.</summary>
    public string NormalizedSku { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public Guid? CategoryId { get; private set; }

    public string Unit { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal? Cost { get; private set; }

    /// <summary>Percentage, e.g. 21.00.</summary>
    public decimal TaxRate { get; private set; }

    public bool TrackInventory { get; private set; }

    public decimal MinStock { get; private set; }

    public string SearchText { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public uint Version { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    public void Update(ProductDetails details, Guid updatedBy, DateTimeOffset now)
    {
        Apply(details);
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }

    public void SetCost(decimal? cost)
    {
        if (cost is { } value && !Amounts.IsValidMoney(value))
        {
            throw new DomainException("amount_invalid", "El coste no es válido.");
        }

        Cost = cost;
    }

    public void Archive(DateTimeOffset now)
    {
        if (IsArchived)
        {
            throw new DomainException("product_archived", "El producto ya está archivado.");
        }

        ArchivedAt = now;
    }

    public void Restore()
    {
        if (!IsArchived)
        {
            throw new DomainException("product_not_archived", "El producto no está archivado.");
        }

        ArchivedAt = null;
    }

    private void Apply(ProductDetails details)
    {
        if (details.Kind == ProductKind.Service && details.TrackInventory)
        {
            throw new DomainException("service_without_stock", "Los servicios no llevan control de existencias.");
        }

        if (!Amounts.IsValidMoney(details.UnitPrice) || !Amounts.IsValidRate(details.TaxRate) ||
            !Amounts.IsValidQuantity(details.MinStock))
        {
            throw new DomainException("amount_invalid", "Algún importe, tipo impositivo o cantidad no es válido.");
        }

        if (!Units.IsKnown(details.Unit))
        {
            throw new DomainException("unit_unknown", "La unidad de medida no es válida.");
        }

        Kind = details.Kind;
        Sku = details.Sku.Trim();
        NormalizedSku = Sku.ToUpperInvariant();
        Name = details.Name.Trim();
        Description = TextNormalization.Optional(details.Description);
        CategoryId = details.CategoryId;
        Unit = details.Unit;
        UnitPrice = details.UnitPrice;
        TaxRate = details.TaxRate;
        TrackInventory = details.TrackInventory;
        MinStock = details.TrackInventory ? details.MinStock : 0;
        SearchText = TextNormalization.SearchDocument(Sku, Name);
    }
}
