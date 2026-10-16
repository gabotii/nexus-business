using System.Text.RegularExpressions;
using FluentValidation;
using Nexus.Domain.Common;

namespace Nexus.Application.Common;

/// <summary>A postal address as sent and returned by the API.</summary>
public sealed record AddressDto(string? Line, string? City, string? PostalCode, string? Region, string? CountryCode)
{
    public static AddressDto From(PostalAddress a) => new(a.Line, a.City, a.PostalCode, a.Region, a.CountryCode);

    public PostalAddress ToDomain() => new(Line, City, PostalCode, Region, CountryCode);
}

internal sealed class AddressValidator : AbstractValidator<AddressDto>
{
    public AddressValidator()
    {
        RuleFor(a => a.Line).MaximumLength(PostalAddress.LineMaxLength)
            .WithMessage($"La dirección admite como máximo {PostalAddress.LineMaxLength} caracteres.");
        RuleFor(a => a.City).MaximumLength(PostalAddress.CityMaxLength)
            .WithMessage($"La localidad admite como máximo {PostalAddress.CityMaxLength} caracteres.");
        RuleFor(a => a.PostalCode).MaximumLength(PostalAddress.PostalCodeMaxLength)
            .WithMessage($"El código postal admite como máximo {PostalAddress.PostalCodeMaxLength} caracteres.");
        RuleFor(a => a.Region).MaximumLength(PostalAddress.RegionMaxLength)
            .WithMessage($"La provincia admite como máximo {PostalAddress.RegionMaxLength} caracteres.");
        RuleFor(a => a.CountryCode)
            .Must(c => PostalAddress.IsValidCountryCode(TextNormalization.Optional(c)))
            .WithMessage("El país debe ser un código ISO de dos letras (p. ej. ES).");
    }
}

internal static partial class ContactRules
{
    [GeneratedRegex(@"^\+?[0-9 ().\-]{6,40}$")]
    private static partial Regex PhonePattern();

    /// <summary>Optional e-mail: empty is fine, anything else must look like an address.</summary>
    public static IRuleBuilderOptions<T, string?> OptionalEmail<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(254).WithMessage("El correo electrónico es demasiado largo.")
            .Must(e => string.IsNullOrWhiteSpace(e) || LooksLikeEmail(e.Trim()))
            .WithMessage("El correo electrónico no es válido.");

    private static bool LooksLikeEmail(string value) =>
        !value.Contains(' ', StringComparison.Ordinal) && System.Net.Mail.MailAddress.TryCreate(value, out var parsed) &&
        parsed.Address == value;

    public static IRuleBuilderOptions<T, string?> OptionalPhone<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(p => string.IsNullOrWhiteSpace(p) || PhonePattern().IsMatch(p.Trim()))
            .WithMessage("El teléfono solo puede contener números, espacios, paréntesis, guiones y un + inicial.");

    public static IRuleBuilderOptions<T, string?> OptionalText<T>(this IRuleBuilder<T, string?> rule, int max, string field) =>
        rule.MaximumLength(max).WithMessage($"{field} admite como máximo {max} caracteres.");

    public static IRuleBuilderOptions<T, string> RequiredName<T>(this IRuleBuilderInitial<T, string> rule, int max, string missing) =>
        rule.Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(missing)
            .Must(n => n.Trim().Length is >= 2 && n.Trim().Length <= max)
            .WithMessage($"El nombre debe tener entre 2 y {max} caracteres.");
}
