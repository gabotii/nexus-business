using System.Globalization;
using System.Text;
using Nexus.Domain.Common;
using Nexus.Domain.Customers;

namespace Nexus.Domain.Tenancy;

public enum TenantStatus
{
    Active,
    Suspended,
}

/// <summary>
/// A company. Its identifier is the isolation boundary for all business data (ADR-0004). Phase 2 stores what a
/// workspace needs to exist plus the company settings (fiscal and contact details, duplicate rule, logo).
/// </summary>
public sealed class Tenant
{
    public const int NameMaxLength = 100;
    public const int SlugMaxLength = 48;

    private Tenant()
    {
        Name = Slug = Currency = TimeZone = Locale = string.Empty;
    }

    public Tenant(Guid id, string name, string slug, string currency, string timeZone, Guid createdBy, DateTimeOffset now)
    {
        if (!Currencies.IsSupported(currency))
        {
            throw new DomainException("currency_not_supported", "La moneda indicada no está admitida.");
        }

        Id = id;
        Name = name.Trim();
        Slug = slug;
        Currency = currency;
        TimeZone = timeZone;
        Locale = "es-ES";
        Status = TenantStatus.Active;
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    /// <summary>URL identifier used by the web app (<c>/c/{slug}</c>). Unique across companies.</summary>
    public string Slug { get; private set; }

    /// <summary>ISO 4217 code. Becomes immutable once sales exist (Phase 4).</summary>
    public string Currency { get; private set; }

    /// <summary>IANA time zone used for business dates (daily totals, reports).</summary>
    public string TimeZone { get; private set; }

    /// <summary>BCP 47 locale for formatting. Spanish only in v1.</summary>
    public string Locale { get; private set; }

    public TenantStatus Status { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; private set; }

    public string? LegalName { get; private set; }

    public string? TaxId { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public string? AddressLine { get; private set; }

    public string? City { get; private set; }

    public string? PostalCode { get; private set; }

    public string? Region { get; private set; }

    public string? CountryCode { get; private set; }

    public CustomerDuplicateRule CustomerDuplicateRule { get; private set; } = CustomerDuplicateRule.EmailOrTaxId;

    /// <summary>The current logo (<c>stored_files</c>), or null.</summary>
    public Guid? LogoFileId { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public PostalAddress Address => new(AddressLine, City, PostalCode, Region, CountryCode);

    /// <summary>
    /// Replaces the company settings. The slug never changes (bookmarks keep working). The currency can still change
    /// while there are no sales; from Phase 4 the sales module refuses it once the first order exists.
    /// </summary>
    public void UpdateSettings(TenantSettings settings, DateTimeOffset now)
    {
        if (!Currencies.IsSupported(settings.Currency))
        {
            throw new DomainException("currency_not_supported", "La moneda indicada no está admitida.");
        }

        var address = settings.Address.Normalized();
        Name = settings.Name.Trim();
        LegalName = TextNormalization.Optional(settings.LegalName);
        TaxId = TextNormalization.Optional(settings.TaxId);
        Email = TextNormalization.Optional(settings.Email);
        Phone = TextNormalization.Optional(settings.Phone);
        AddressLine = address.Line;
        City = address.City;
        PostalCode = address.PostalCode;
        Region = address.Region;
        CountryCode = address.CountryCode;
        Currency = settings.Currency;
        TimeZone = settings.TimeZone;
        CustomerDuplicateRule = settings.CustomerDuplicateRule;
        UpdatedAt = now;
    }

    public void SetLogo(Guid? fileId, DateTimeOffset now)
    {
        LogoFileId = fileId;
        UpdatedAt = now;
    }

    /// <summary>
    /// "Panadería Núñez & Hijos" → "panaderia-nunez-hijos". Only <c>[a-z0-9-]</c>, no leading/trailing or doubled
    /// hyphens; falls back to "empresa" when nothing usable remains.
    /// </summary>
    public static string SlugFrom(string name)
    {
        var decomposed = name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > SlugMaxLength - 5)
        {
            // Leave room for a disambiguation suffix ("-a1b2").
            slug = slug[..(SlugMaxLength - 5)].TrimEnd('-');
        }

        return slug.Length == 0 ? "empresa" : slug;
    }
}

/// <summary>The editable company settings, already validated by the use case.</summary>
public sealed record TenantSettings(
    string Name,
    string? LegalName,
    string? TaxId,
    string? Email,
    string? Phone,
    PostalAddress Address,
    string Currency,
    string TimeZone,
    CustomerDuplicateRule CustomerDuplicateRule);
