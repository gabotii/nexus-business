using System.Security.Cryptography;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexus.Application.Abstractions;
using Nexus.Application.Common;
using Nexus.Domain.Common;
using Nexus.Domain.Customers;
using Nexus.Domain.Files;
using Nexus.Domain.Tenancy;

namespace Nexus.Application.Tenancy;

public sealed record TenantSettingsDto(
    Guid Id,
    string Name,
    string Slug,
    string? LegalName,
    string? TaxId,
    string? Email,
    string? Phone,
    AddressDto Address,
    string Currency,
    string TimeZone,
    string Locale,
    CustomerDuplicateRule CustomerDuplicateRule,
    Guid? LogoFileId,
    uint Version);

public sealed record UpdateTenantSettingsRequest(
    string Name,
    string? LegalName,
    string? TaxId,
    string? Email,
    string? Phone,
    AddressDto? Address,
    string Currency,
    string TimeZone,
    CustomerDuplicateRule CustomerDuplicateRule,
    uint Version);

/// <summary>The stored logo, ready to stream.</summary>
public sealed record LogoContent(Stream Content, string ContentType);

internal sealed class UpdateTenantSettingsRequestValidator : AbstractValidator<UpdateTenantSettingsRequest>
{
    public UpdateTenantSettingsRequestValidator()
    {
        RuleFor(r => r.Name).RequiredName(Tenant.NameMaxLength, "Indica el nombre de la empresa.");
        RuleFor(r => r.LegalName).OptionalText(200, "La razón social");
        RuleFor(r => r.TaxId).OptionalText(32, "El NIF/CIF");
        RuleFor(r => r.Email).OptionalEmail();
        RuleFor(r => r.Phone).OptionalPhone();
        RuleFor(r => r.Address!).SetValidator(new AddressValidator()).When(r => r.Address is not null);
        RuleFor(r => r.Currency).Must(Currencies.IsSupported).WithMessage("La moneda indicada no está admitida.");
        RuleFor(r => r.TimeZone).Must(TimeZones.IsValid).WithMessage("La zona horaria no es válida.");
        RuleFor(r => r.CustomerDuplicateRule).IsInEnum().WithMessage("La regla de duplicados no es válida.");
    }
}

/// <summary>Company settings and logo (permission <c>settings.manage</c>; any member may display the logo).</summary>
public sealed class TenantSettingsService(
    IAppDbContext db,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IFileStorage storage,
    IAuditLog audit,
    IValidator<UpdateTenantSettingsRequest> validator,
    TimeProvider clock,
    ILogger<TenantSettingsService> logger)
{
    public async Task<TenantSettingsDto> GetAsync(CancellationToken ct)
    {
        tenant.Demand(Permissions.SettingsManage);
        return ToDto(await LoadAsync(ct));
    }

    public async Task<TenantSettingsDto> UpdateAsync(UpdateTenantSettingsRequest request, CancellationToken ct)
    {
        tenant.Demand(Permissions.SettingsManage);
        await validator.ValidateAndThrowAsync(request, ct);
        var company = await LoadAsync(ct);

        var before = ToDto(company);
        company.UpdateSettings(new TenantSettings(request.Name, request.LegalName, request.TaxId, request.Email, request.Phone,
            (request.Address ?? new AddressDto(null, null, null, null, null)).ToDomain(), request.Currency, request.TimeZone,
            request.CustomerDuplicateRule), clock.GetUtcNow());
        var after = ToDto(company);

        audit.Tenant("settings.updated", "tenant", company.Id, new
        {
            Changed = ChangedFields(before, after),
            Currency = before.Currency == after.Currency ? null : new { From = before.Currency, To = after.Currency },
        });
        await db.SaveExpectingAsync(company, request.Version, ct);
        return ToDto(company);
    }

    /// <summary>
    /// Validates the image by its bytes (PNG, JPEG or WebP; at most 512 KB), stores it under a generated key and
    /// replaces the previous logo. The bytes are written before the database row, and removed again if saving fails.
    /// </summary>
    public async Task<TenantSettingsDto> SetLogoAsync(ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        tenant.Demand(Permissions.SettingsManage);
        if (content.Length == 0 || content.Length > ImageFormats.MaxLogoBytes)
        {
            throw new DomainException("logo_size", "El logo debe ocupar como máximo 512 KB.");
        }

        var contentType = ImageFormats.Detect(content.Span[..Math.Min(content.Length, 16)])
                          ?? throw new DomainException("logo_format", "El logo debe ser una imagen PNG, JPEG o WebP.");

        var company = await LoadAsync(ct);
        var previous = company.LogoFileId is { } previousId
            ? await db.StoredFiles.SingleOrDefaultAsync(f => f.Id == previousId, ct)
            : null;
        var file = new StoredFile(tenant.TenantId, FilePurpose.TenantLogo, contentType, content.Length,
            SHA256.HashData(content.Span), currentUser.UserId, clock.GetUtcNow());

        await storage.SaveAsync(file.StorageKey, content, ct);
        try
        {
            db.StoredFiles.Add(file);
            company.SetLogo(file.Id, clock.GetUtcNow());
            if (previous is not null)
            {
                db.StoredFiles.Remove(previous);
            }

            audit.Tenant("settings.logo_updated", "tenant", company.Id, new { FileId = file.Id, file.ContentType, file.SizeBytes });
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await storage.DeleteAsync(file.StorageKey, CancellationToken.None);
            throw;
        }

        await DeleteBytesAsync(previous);
        return ToDto(company);
    }

    public async Task RemoveLogoAsync(CancellationToken ct)
    {
        tenant.Demand(Permissions.SettingsManage);
        var company = await LoadAsync(ct);
        if (company.LogoFileId is not { } fileId)
        {
            return;
        }

        var file = await db.StoredFiles.SingleOrDefaultAsync(f => f.Id == fileId, ct);
        company.SetLogo(null, clock.GetUtcNow());
        if (file is not null)
        {
            db.StoredFiles.Remove(file);
        }

        audit.Tenant("settings.logo_removed", "tenant", company.Id);
        await db.SaveChangesAsync(ct);
        await DeleteBytesAsync(file);
    }

    /// <summary>Any member may load the logo of their company.</summary>
    public async Task<LogoContent> OpenLogoAsync(CancellationToken ct)
    {
        var file = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenant.TenantId && t.LogoFileId != null)
            .Join(db.StoredFiles, t => t.LogoFileId, f => f.Id, (_, f) => f)
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("La empresa no tiene logo.");
        var stream = await storage.OpenAsync(file.StorageKey, ct) ?? throw new NotFoundException("La empresa no tiene logo.");
        return new LogoContent(stream, file.ContentType);
    }

    private async Task<Tenant> LoadAsync(CancellationToken ct) => await db.Tenants.SingleAsync(t => t.Id == tenant.TenantId, ct);

    private async Task DeleteBytesAsync(StoredFile? file)
    {
        if (file is null)
        {
            return;
        }

        try
        {
            await storage.DeleteAsync(file.StorageKey, CancellationToken.None);
        }
        catch (IOException ex)
        {
            // The row is gone; an orphaned file is harmless and only costs disk space.
            logger.LogWarning(ex, "Could not delete stored file {FileId}", file.Id);
        }
    }

    private static TenantSettingsDto ToDto(Tenant t) => new(t.Id, t.Name, t.Slug, t.LegalName, t.TaxId, t.Email, t.Phone,
        AddressDto.From(t.Address), t.Currency, t.TimeZone, t.Locale, t.CustomerDuplicateRule, t.LogoFileId, t.Version);

    private static string[] ChangedFields(TenantSettingsDto before, TenantSettingsDto after) =>
    [
        .. new (string Name, bool Changed)[]
        {
            ("name", before.Name != after.Name),
            ("legalName", before.LegalName != after.LegalName),
            ("taxId", before.TaxId != after.TaxId),
            ("email", before.Email != after.Email),
            ("phone", before.Phone != after.Phone),
            ("address", before.Address != after.Address),
            ("currency", before.Currency != after.Currency),
            ("timeZone", before.TimeZone != after.TimeZone),
            ("customerDuplicateRule", before.CustomerDuplicateRule != after.CustomerDuplicateRule),
        }.Where(x => x.Changed).Select(x => x.Name),
    ];
}
