using FluentValidation;
using Nexus.Application.Common;
using Nexus.Domain.Customers;

namespace Nexus.Application.Customers;

public sealed record TagRef(Guid Id, string Name, string Color);

public sealed record TagDto(Guid Id, string Name, string Color, int CustomerCount);

public sealed record SaveTagRequest(string Name, string Color);

public sealed record CustomerListItem(
    Guid Id,
    CustomerKind Kind,
    string Name,
    string? LegalName,
    string? Email,
    string? Phone,
    string? TaxId,
    string? City,
    IReadOnlyList<TagRef> Tags,
    bool Archived,
    DateTimeOffset CreatedAt);

public sealed record CustomerDto(
    Guid Id,
    CustomerKind Kind,
    string Name,
    string? LegalName,
    string? TaxId,
    string? Email,
    string? Phone,
    AddressDto Address,
    IReadOnlyList<TagRef> Tags,
    bool Archived,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    uint Version);

/// <summary>Create (no <see cref="Version"/>) or update (the version the client read; required).</summary>
public sealed record SaveCustomerRequest(
    CustomerKind Kind,
    string Name,
    string? LegalName,
    string? TaxId,
    string? Email,
    string? Phone,
    AddressDto? Address,
    IReadOnlyList<Guid>? TagIds,
    uint? Version);

public enum CustomerSort
{
    Name,
    Recent,
}

public sealed record CustomerQuery(
    string? Q = null,
    Guid? TagId = null,
    bool Archived = false,
    CustomerSort Sort = CustomerSort.Name,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

public sealed record CustomerNoteDto(Guid Id, string Body, string AuthorName, bool IsYours, DateTimeOffset CreatedAt);

public sealed record AddNoteRequest(string Body);

internal sealed class SaveCustomerRequestValidator : AbstractValidator<SaveCustomerRequest>
{
    public SaveCustomerRequestValidator()
    {
        RuleFor(r => r.Kind).IsInEnum().WithMessage("El tipo de cliente no es válido.");
        RuleFor(r => r.Name).RequiredName(Customer.NameMaxLength, "Indica el nombre del cliente.");
        RuleFor(r => r.LegalName).OptionalText(Customer.LegalNameMaxLength, "La razón social");
        RuleFor(r => r.TaxId).OptionalText(Customer.TaxIdMaxLength, "El NIF/CIF");
        RuleFor(r => r.Email).OptionalEmail();
        RuleFor(r => r.Phone).OptionalPhone();
        RuleFor(r => r.Address!).SetValidator(new AddressValidator()).When(r => r.Address is not null);
        RuleFor(r => r.TagIds)
            .Must(ids => ids is null || ids.Count <= Customer.MaxTags)
            .WithMessage($"Un cliente admite como máximo {Customer.MaxTags} etiquetas.");
    }
}

internal sealed class CustomerQueryValidator : AbstractValidator<CustomerQuery>
{
    public CustomerQueryValidator()
    {
        RuleFor(q => q.Q).MaximumLength(100).WithMessage("La búsqueda es demasiado larga.");
        RuleFor(q => q.Page).ValidPage();
        RuleFor(q => q.PageSize).ValidPageSize();
        RuleFor(q => q.Sort).IsInEnum().WithMessage("El orden no es válido.");
    }
}

internal sealed class SaveTagRequestValidator : AbstractValidator<SaveTagRequest>
{
    public SaveTagRequestValidator()
    {
        RuleFor(r => r.Name).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica el nombre de la etiqueta.")
            .Must(n => n.Trim().Length is >= 1 && n.Trim().Length <= Tag.NameMaxLength)
            .WithMessage($"El nombre admite como máximo {Tag.NameMaxLength} caracteres.");
        RuleFor(r => r.Color).Must(c => Tag.Colors.Contains(c)).WithMessage("El color de la etiqueta no es válido.");
    }
}

internal sealed class AddNoteRequestValidator : AbstractValidator<AddNoteRequest>
{
    public AddNoteRequestValidator() =>
        RuleFor(r => r.Body).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Escribe la nota.")
            .Must(b => b.Trim().Length <= CustomerNote.BodyMaxLength)
            .WithMessage($"La nota admite como máximo {CustomerNote.BodyMaxLength} caracteres.");
}
