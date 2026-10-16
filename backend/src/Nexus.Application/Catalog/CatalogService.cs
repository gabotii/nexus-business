using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Nexus.Application.Abstractions;
using Nexus.Application.Common;
using Nexus.Application.Tenancy;
using Nexus.Domain.Catalog;
using Nexus.Domain.Common;
using Nexus.Domain.Tenancy;

namespace Nexus.Application.Catalog;

/// <summary>Product categories and products of the current company.</summary>
public sealed class CatalogService(
    IAppDbContext db,
    IDatabaseTools tools,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IAuditLog audit,
    IValidator<SaveCategoryRequest> categoryValidator,
    IValidator<SaveProductRequest> productValidator,
    IValidator<ProductQuery> queryValidator,
    TimeProvider clock)
{
    internal const string SkuConstraint = "ux_products_tenant_sku";
    internal const string CategoryNameConstraint = "ux_product_categories_tenant_name";

    private bool CanSeeCost => tenant.Has(Permissions.ProductsViewCost);

    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken ct)
    {
        tenant.Demand(Permissions.ProductsView);
        var categories = await db.ProductCategories.AsNoTracking()
            .Select(c => new CategoryDto(c.Id, c.Name, c.ArchivedAt != null,
                db.Products.Count(p => p.CategoryId == c.Id && p.ArchivedAt == null)))
            .ToListAsync(ct);
        return [.. categories.OrderBy(c => c.Archived).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    public async Task<CategoryDto> CreateCategoryAsync(SaveCategoryRequest request, CancellationToken ct)
    {
        tenant.Demand(Permissions.ProductsManage);
        await categoryValidator.ValidateAndThrowAsync(request, ct);
        await EnsureCategoryNameFreeAsync(request.Name, null, ct);
        var category = new ProductCategory(tenant.TenantId, request.Name, clock.GetUtcNow());
        db.ProductCategories.Add(category);
        audit.Tenant("categories.created", "category", category.Id, new { category.Name });
        await SaveAsync(ct);
        return new CategoryDto(category.Id, category.Name, false, 0);
    }

    public async Task<CategoryDto> RenameCategoryAsync(Guid categoryId, SaveCategoryRequest request, CancellationToken ct)
    {
        tenant.Demand(Permissions.ProductsManage);
        await categoryValidator.ValidateAndThrowAsync(request, ct);
        var category = await FindCategoryAsync(categoryId, ct);
        await EnsureCategoryNameFreeAsync(request.Name, category.Id, ct);
        category.Rename(request.Name);
        audit.Tenant("categories.renamed", "category", category.Id, new { category.Name });
        await SaveAsync(ct);
        return new CategoryDto(category.Id, category.Name, category.IsArchived,
            await db.Products.CountAsync(p => p.CategoryId == category.Id && p.ArchivedAt == null, ct));
    }

    /// <summary>Archived categories keep their products but cannot be assigned to new ones.</summary>
    public async Task SetCategoryArchivedAsync(Guid categoryId, bool archived, CancellationToken ct)
    {
        tenant.Demand(Permissions.ProductsManage);
        var category = await FindCategoryAsync(categoryId, ct);
        if (archived)
        {
            category.Archive(clock.GetUtcNow());
        }
        else
        {
            category.Restore();
        }

        audit.Tenant(archived ? "categories.archived" : "categories.restored", "category", category.Id);
        await db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<ProductListItem>> ListProductsAsync(ProductQuery query, CancellationToken ct)
    {
        tenant.Demand(Permissions.ProductsView);
        await queryValidator.ValidateAndThrowAsync(query, ct);

        var products = db.Products.AsNoTracking().Where(p => (p.ArchivedAt != null) == query.Archived);
        var term = TextNormalization.ForSearch(query.Q);
        if (term.Length > 0)
        {
            var pattern = Paging.ContainsPattern(term);
            products = products.Where(p => EF.Functions.Like(p.SearchText, pattern, "\\"));
        }

        if (query.CategoryId is { } categoryId)
        {
            products = products.Where(p => p.CategoryId == categoryId);
        }

        if (query.Kind is { } kind)
        {
            products = products.Where(p => p.Kind == kind);
        }

        var canSeeCost = CanSeeCost;
        var page = await products
            .OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Select(p => new ProductListItem(p.Id, p.Kind, p.Sku, p.Name,
                p.CategoryId == null
                    ? null
                    : db.ProductCategories.Where(c => c.Id == p.CategoryId).Select(c => new CategoryRef(c.Id, c.Name)).First(),
                p.Unit, p.UnitPrice, p.TaxRate, canSeeCost ? p.Cost : null, p.TrackInventory, p.ArchivedAt != null))
            .ToPageAsync(query.Page, query.PageSize, ct);
        return page;
    }

    public async Task<ProductDto> GetProductAsync(Guid productId, CancellationToken ct)
    {
        tenant.Demand(Permissions.ProductsView);
        return await ToDtoAsync(await FindProductAsync(productId, ct), ct);
    }

    public async Task<ProductDto> CreateProductAsync(SaveProductRequest request, CancellationToken ct)
    {
        tenant.Demand(Permissions.ProductsManage);
        await productValidator.ValidateAndThrowAsync(request, ct);
        await EnsureCategoryAssignableAsync(request.CategoryId, currentCategoryId: null, ct);
        await EnsureSkuFreeAsync(request.Sku, null, ct);

        var product = new Product(tenant.TenantId, ToDetails(request), CanSeeCost ? request.Cost : null,
            currentUser.UserId, clock.GetUtcNow());
        db.Products.Add(product);
        audit.Tenant("products.created", "product", product.Id, new { product.Sku, product.UnitPrice, product.TaxRate });
        await SaveAsync(ct);
        return await ToDtoAsync(product, ct);
    }

    public async Task<ProductDto> UpdateProductAsync(Guid productId, SaveProductRequest request, CancellationToken ct)
    {
        tenant.Demand(Permissions.ProductsManage);
        await productValidator.ValidateAndThrowAsync(request, ct);
        var version = request.Version ?? throw new ValidationException(
            [new FluentValidation.Results.ValidationFailure("version", "Falta la versión del registro.")]);
        var product = await FindProductAsync(productId, ct);
        await EnsureCategoryAssignableAsync(request.CategoryId, product.CategoryId, ct);
        await EnsureSkuFreeAsync(request.Sku, product.Id, ct);

        var (oldPrice, oldRate, oldCost) = (product.UnitPrice, product.TaxRate, product.Cost);
        product.Update(ToDetails(request), currentUser.UserId, clock.GetUtcNow());
        if (CanSeeCost)
        {
            product.SetCost(request.Cost);
        }

        // Price and tax changes are business-relevant and safe to record; the cost is only flagged.
        audit.Tenant("products.updated", "product", product.Id, new
        {
            product.Sku,
            Price = oldPrice == product.UnitPrice ? null : new { From = oldPrice, To = product.UnitPrice },
            TaxRate = oldRate == product.TaxRate ? null : new { From = oldRate, To = product.TaxRate },
            CostChanged = oldCost != product.Cost,
        });
        try
        {
            await db.SaveExpectingAsync(product, version, ct);
        }
        catch (DbUpdateException ex) when (tools.IsUniqueViolation(ex, SkuConstraint))
        {
            throw SkuTaken();
        }

        return await ToDtoAsync(product, ct);
    }

    public async Task SetProductArchivedAsync(Guid productId, bool archived, CancellationToken ct)
    {
        tenant.Demand(Permissions.ProductsManage);
        var product = await FindProductAsync(productId, ct);
        if (archived)
        {
            product.Archive(clock.GetUtcNow());
        }
        else
        {
            product.Restore();
        }

        audit.Tenant(archived ? "products.archived" : "products.restored", "product", product.Id);
        await db.SaveChangesAsync(ct);
    }

    private async Task<ProductCategory> FindCategoryAsync(Guid categoryId, CancellationToken ct) =>
        await db.ProductCategories.SingleOrDefaultAsync(c => c.Id == categoryId, ct)
        ?? throw new NotFoundException("La categoría no existe.");

    private async Task<Product> FindProductAsync(Guid productId, CancellationToken ct) =>
        await db.Products.SingleOrDefaultAsync(p => p.Id == productId, ct)
        ?? throw new NotFoundException("El producto no existe.");

    /// <summary>The category must exist here and be active, unless the product already had it.</summary>
    private async Task EnsureCategoryAssignableAsync(Guid? categoryId, Guid? currentCategoryId, CancellationToken ct)
    {
        if (categoryId is not { } id)
        {
            return;
        }

        var category = await FindCategoryAsync(id, ct);
        if (category.IsArchived && id != currentCategoryId)
        {
            throw new DomainException("category_archived", "La categoría está archivada.");
        }
    }

    private async Task EnsureSkuFreeAsync(string sku, Guid? exceptId, CancellationToken ct)
    {
        var normalized = sku.Trim().ToUpperInvariant();
        if (await db.Products.AnyAsync(p => p.NormalizedSku == normalized && p.Id != exceptId, ct))
        {
            throw SkuTaken();
        }
    }

    private async Task EnsureCategoryNameFreeAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var normalized = TextNormalization.ForSearch(name);
        if (await db.ProductCategories.AnyAsync(c => c.NormalizedName == normalized && c.Id != exceptId, ct))
        {
            throw CategoryNameTaken();
        }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (tools.IsUniqueViolation(ex, SkuConstraint))
        {
            throw SkuTaken();
        }
        catch (DbUpdateException ex) when (tools.IsUniqueViolation(ex, CategoryNameConstraint))
        {
            throw CategoryNameTaken();
        }
    }

    private async Task<ProductDto> ToDtoAsync(Product p, CancellationToken ct)
    {
        var category = p.CategoryId is { } id
            ? await db.ProductCategories.AsNoTracking().Where(c => c.Id == id).Select(c => new CategoryRef(c.Id, c.Name)).SingleAsync(ct)
            : null;
        return new ProductDto(p.Id, p.Kind, p.Sku, p.Name, p.Description, category, p.Unit, p.UnitPrice, p.TaxRate,
            CanSeeCost ? p.Cost : null, p.TrackInventory, p.MinStock, p.IsArchived, p.CreatedAt, p.UpdatedAt, p.Version);
    }

    private static ProductDetails ToDetails(SaveProductRequest r) =>
        new(r.Kind, r.Sku, r.Name, r.Description, r.CategoryId, r.Unit, r.UnitPrice, r.TaxRate, r.TrackInventory, r.MinStock);

    private static ConflictException SkuTaken() => new("sku_taken", "Ya existe un producto con ese SKU.");

    private static ConflictException CategoryNameTaken() =>
        new("category_name_taken", "Ya existe una categoría con ese nombre.");
}
