using Nexus.Domain.Common;

namespace Nexus.Domain.Customers;

public enum CustomerKind
{
    Person,
    Company,
}

/// <summary>The editable data of a customer, already validated by the use case.</summary>
public sealed record CustomerDetails(
    CustomerKind Kind,
    string Name,
    string? LegalName,
    string? TaxId,
    string? Email,
    string? Phone,
    PostalAddress Address);

/// <summary>
/// A customer of the company. Never deleted (sales will reference it): archived instead. <see cref="SearchText"/>,
/// <see cref="NormalizedEmail"/> and <see cref="NormalizedTaxId"/> are derived on every change and back the search
/// index and the duplicate rule.
/// </summary>
public sealed class Customer : ITenantOwned
{
    public const int NameMaxLength = 150;
    public const int LegalNameMaxLength = 200;
    public const int TaxIdMaxLength = 32;
    public const int EmailMaxLength = 254;
    public const int PhoneMaxLength = 40;
    public const int MaxTags = 20;

    private readonly List<CustomerTag> _tags = [];

    private Customer()
    {
        Name = SearchText = string.Empty;
    }

    public Customer(Guid tenantId, CustomerDetails details, Guid createdBy, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        CreatedAt = now;
        CreatedBy = createdBy;
        Name = SearchText = string.Empty;
        Apply(details);
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public CustomerKind Kind { get; private set; }

    public string Name { get; private set; }

    public string? LegalName { get; private set; }

    public string? TaxId { get; private set; }

    public string? NormalizedTaxId { get; private set; }

    public string? Email { get; private set; }

    public string? NormalizedEmail { get; private set; }

    public string? Phone { get; private set; }

    public string? AddressLine { get; private set; }

    public string? City { get; private set; }

    public string? PostalCode { get; private set; }

    public string? Region { get; private set; }

    public string? CountryCode { get; private set; }

    public string SearchText { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyCollection<CustomerTag> Tags => _tags;

    public bool IsArchived => ArchivedAt is not null;

    public PostalAddress Address => new(AddressLine, City, PostalCode, Region, CountryCode);

    public void Update(CustomerDetails details, Guid updatedBy, DateTimeOffset now)
    {
        Apply(details);
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }

    /// <summary>Replaces the tag set. The tags must belong to the same company (checked by the use case and the FK).</summary>
    public void SetTags(IReadOnlyCollection<Guid> tagIds)
    {
        if (tagIds.Count > MaxTags)
        {
            throw new DomainException("too_many_tags", $"Un cliente admite como máximo {MaxTags} etiquetas.");
        }

        _tags.RemoveAll(t => !tagIds.Contains(t.TagId));
        foreach (var tagId in tagIds.Where(id => _tags.All(t => t.TagId != id)))
        {
            _tags.Add(new CustomerTag(TenantId, Id, tagId));
        }
    }

    public void Archive(DateTimeOffset now)
    {
        if (IsArchived)
        {
            throw new DomainException("customer_archived", "El cliente ya está archivado.");
        }

        ArchivedAt = now;
    }

    public void Restore()
    {
        if (!IsArchived)
        {
            throw new DomainException("customer_not_archived", "El cliente no está archivado.");
        }

        ArchivedAt = null;
    }

    private void Apply(CustomerDetails details)
    {
        var address = details.Address.Normalized();
        Kind = details.Kind;
        Name = details.Name.Trim();
        LegalName = TextNormalization.Optional(details.LegalName);
        TaxId = TextNormalization.Optional(details.TaxId);
        NormalizedTaxId = TextNormalization.TaxId(details.TaxId);
        Email = TextNormalization.Optional(details.Email);
        NormalizedEmail = TextNormalization.Email(details.Email);
        Phone = TextNormalization.Optional(details.Phone);
        AddressLine = address.Line;
        City = address.City;
        PostalCode = address.PostalCode;
        Region = address.Region;
        CountryCode = address.CountryCode;
        SearchText = TextNormalization.SearchDocument(Name, LegalName, Email, TaxId, NormalizedTaxId, Phone, City);
    }
}

/// <summary>Customer ↔ tag link. Carries the tenant for both composite FKs and Row-Level Security.</summary>
public sealed class CustomerTag : ITenantOwned
{
    private CustomerTag()
    {
    }

    internal CustomerTag(Guid tenantId, Guid customerId, Guid tagId)
    {
        TenantId = tenantId;
        CustomerId = customerId;
        TagId = tagId;
    }

    public Guid TenantId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid TagId { get; private set; }
}

/// <summary>An internal note about a customer. Free text: never exported nor written to the audit log.</summary>
public sealed class CustomerNote : ITenantOwned
{
    public const int BodyMaxLength = 2000;

    private CustomerNote()
    {
        Body = string.Empty;
    }

    public CustomerNote(Guid tenantId, Guid customerId, string body, Guid authorId, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        CustomerId = customerId;
        Body = body.Trim();
        AuthorId = authorId;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid CustomerId { get; private set; }

    public string Body { get; private set; }

    public Guid AuthorId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>A label to group customers ("Mayorista", "VIP"…). Colours come from a fixed, accessible palette.</summary>
public sealed class Tag : ITenantOwned
{
    public const int NameMaxLength = 40;

    public static readonly IReadOnlyList<string> Colors = ["slate", "red", "orange", "amber", "green", "teal", "blue", "violet", "pink"];

    private Tag()
    {
        Name = NormalizedName = Color = string.Empty;
    }

    public Tag(Guid tenantId, string name, string color, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        CreatedAt = now;
        Name = NormalizedName = Color = string.Empty;
        Update(name, color);
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; }

    /// <summary>Unique per company: names differing only in case or accents are the same tag.</summary>
    public string NormalizedName { get; private set; }

    public string Color { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Update(string name, string color)
    {
        if (!Colors.Contains(color))
        {
            throw new DomainException("tag_color_unknown", "El color de la etiqueta no es válido.");
        }

        Name = name.Trim();
        NormalizedName = TextNormalization.ForSearch(name);
        Color = color;
    }
}

/// <summary>How a company detects duplicate customers (company setting).</summary>
public enum CustomerDuplicateRule
{
    None,
    Email,
    TaxId,
    EmailOrTaxId,
}
