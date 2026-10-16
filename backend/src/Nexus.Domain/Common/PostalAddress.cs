namespace Nexus.Domain.Common;

/// <summary>A postal address; every part is optional. Stored as plain columns on its owner.</summary>
public sealed record PostalAddress(string? Line, string? City, string? PostalCode, string? Region, string? CountryCode)
{
    public const int LineMaxLength = 200;
    public const int CityMaxLength = 100;
    public const int PostalCodeMaxLength = 20;
    public const int RegionMaxLength = 100;

    public static readonly PostalAddress Empty = new(null, null, null, null, null);

    /// <summary>Trims every part, turns blanks into null and upper-cases the ISO 3166-1 alpha-2 country code.</summary>
    public PostalAddress Normalized() => new(
        TextNormalization.Optional(Line),
        TextNormalization.Optional(City),
        TextNormalization.Optional(PostalCode),
        TextNormalization.Optional(Region),
        TextNormalization.Optional(CountryCode)?.ToUpperInvariant());

    /// <summary>Two ASCII letters (ISO 3166-1 alpha-2). The list of countries itself is the client's concern.</summary>
    public static bool IsValidCountryCode(string? code) =>
        code is null || (code.Length == 2 && code.All(char.IsAsciiLetter));
}
