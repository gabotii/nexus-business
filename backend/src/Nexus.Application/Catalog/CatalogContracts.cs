using FluentValidation;
using Nexus.Application.Common;
using Nexus.Domain.Catalog;
using Nexus.Domain.Common;

namespace Nexus.Application.Catalog;

public sealed record CategoryRef(Guid Id, string Name);

public sealed record CategoryDto(Guid Id, string Name, bool Archived, int ProductCount);

public sealed record SaveCategoryRequest(string Name);

/// <param name="Cost">Only for callers with <c>products.view_cost</c>; null otherwise.</param>
public sealed record ProductListItem(
    Guid Id,
    ProductKind Kind,
    string Sku,
    string Name,
    CategoryRef? Category,
    string Unit,
    decimal UnitPrice,
    decimal TaxRate,
    decimal? Cost,
    bool TrackInventory,
    bool Archived);

public sealed record ProductDto(
    Guid Id,
    ProductKind Kind,
    string Sku,
    string Name,
    string? Description,
    CategoryRef? Category,
    string Unit,
    decimal UnitPrice,
    decimal TaxRate,
    decimal? Cost,
    bool TrackInventory,
    decimal MinStock,
    bool Archived,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    uint Version);

/// <summary>
/// Create (no <see cref="Version"/>) or update. <see cref="Cost"/> is ignored for callers without
/// <c>products.view_cost</c>: they can neither see nor overwrite it.
/// </summary>
public sealed record SaveProductRequest(
    ProductKind Kind,
    string Sku,
    string Name,
    string? Description,
    Guid? CategoryId,
    string Unit,
    decimal UnitPrice,
    decimal? Cost,
    decimal TaxRate,
    bool TrackInventory,
    decimal MinStock,
    uint? Version);

public sealed record ProductQuery(
    string? Q = null,
    Guid? CategoryId = null,
    ProductKind? Kind = null,
    bool Archived = false,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

internal sealed class SaveCategoryRequestValidator : AbstractValidator<SaveCategoryRequest>
{
    public SaveCategoryRequestValidator() =>
        RuleFor(r => r.Name).RequiredName(ProductCategory.NameMaxLength, "Indica el nombre de la categoría.");
}

internal sealed class SaveProductRequestValidator : AbstractValidator<SaveProductRequest>
{
    private const string SkuCharacters = "El SKU solo admite letras, números, guiones, puntos y guiones bajos.";

    public SaveProductRequestValidator()
    {
        RuleFor(r => r.Kind).IsInEnum().WithMessage("El tipo no es válido.");
        RuleFor(r => r.Sku).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica el SKU (código interno).")
            .MaximumLength(Product.SkuMaxLength).WithMessage($"El SKU admite como máximo {Product.SkuMaxLength} caracteres.")
            .Must(s => s.Trim().All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_')).WithMessage(SkuCharacters);
        RuleFor(r => r.Name).RequiredName(Product.NameMaxLength, "Indica el nombre del producto.");
        RuleFor(r => r.Description).OptionalText(Product.DescriptionMaxLength, "La descripción");
        RuleFor(r => r.Unit).Must(Units.IsKnown).WithMessage("La unidad de medida no es válida.");
        RuleFor(r => r.UnitPrice).Must(Amounts.IsValidMoney)
            .WithMessage("El precio debe ser cero o positivo, con un máximo de 4 decimales.");
        RuleFor(r => r.Cost).Must(c => c is null || Amounts.IsValidMoney(c.Value))
            .WithMessage("El coste debe ser cero o positivo, con un máximo de 4 decimales.");
        RuleFor(r => r.TaxRate).Must(Amounts.IsValidRate)
            .WithMessage("El impuesto debe estar entre 0 y 100, con un máximo de 2 decimales.");
        RuleFor(r => r.MinStock).Must(Amounts.IsValidQuantity)
            .WithMessage("El stock mínimo debe ser cero o positivo, con un máximo de 3 decimales.");
        RuleFor(r => r.TrackInventory).Equal(false).When(r => r.Kind == ProductKind.Service)
            .WithMessage("Los servicios no llevan control de existencias.");
    }
}

internal sealed class ProductQueryValidator : AbstractValidator<ProductQuery>
{
    public ProductQueryValidator()
    {
        RuleFor(q => q.Q).MaximumLength(100).WithMessage("La búsqueda es demasiado larga.");
        RuleFor(q => q.Kind).IsInEnum().When(q => q.Kind is not null).WithMessage("El tipo no es válido.");
        RuleFor(q => q.Page).ValidPage();
        RuleFor(q => q.PageSize).ValidPageSize();
    }
}
