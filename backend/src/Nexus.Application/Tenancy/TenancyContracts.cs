using FluentValidation;
using Nexus.Domain.Common;
using Nexus.Domain.Tenancy;

namespace Nexus.Application.Tenancy;

public sealed record RoleRef(Guid Id, string Name, string? SystemKey);

/// <summary>A company the current user can open, as listed in the company switcher.</summary>
public sealed record MyTenantDto(Guid Id, string Name, string Slug, RoleRef Role);

/// <summary>The opened company plus the caller's role and effective permissions (the SPA uses them for UX only).</summary>
public sealed record WorkspaceDto(
    Guid Id,
    string Name,
    string Slug,
    string Currency,
    string TimeZone,
    string Locale,
    RoleRef Role,
    IReadOnlyList<string> Permissions,
    Guid? LogoFileId);

public sealed record CreateTenantRequest(string Name, string? Currency, string? TimeZone);

public sealed record MemberDto(
    Guid Id,
    Guid UserId,
    string DisplayName,
    string Email,
    RoleRef Role,
    MembershipStatus Status,
    DateTimeOffset JoinedAt,
    bool IsYou);

public sealed record ChangeMemberRoleRequest(Guid RoleId);

public sealed record RoleDto(
    Guid Id,
    string Name,
    string? Description,
    string? SystemKey,
    bool Editable,
    bool Deletable,
    IReadOnlyList<string> Permissions,
    int MemberCount);

public sealed record SaveRoleRequest(string Name, string? Description, IReadOnlyList<string>? Permissions);

public enum InvitationState
{
    Pending,
    Expired,
}

public sealed record InvitationDto(
    Guid Id,
    string Email,
    RoleRef Role,
    InvitationState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string InvitedBy);

public sealed record CreateInvitationRequest(string Email, Guid RoleId);

/// <summary>The two halves of an invitation link: the company it belongs to and the secret token.</summary>
public sealed record InvitationTokenRequest(Guid TenantId, string Token);

public sealed record InvitationPreviewDto(string TenantName, string Email, string RoleName, string InvitedBy,
    DateTimeOffset ExpiresAt);

internal sealed class CreateTenantRequestValidator : AbstractValidator<CreateTenantRequest>
{
    public CreateTenantRequestValidator()
    {
        RuleFor(r => r.Name).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica el nombre de la empresa.")
            .Must(n => n.Trim().Length is >= 2 and <= Tenant.NameMaxLength)
            .WithMessage($"El nombre debe tener entre 2 y {Tenant.NameMaxLength} caracteres.");
        RuleFor(r => r.Currency)
            .Must(c => c is null || Currencies.IsSupported(c))
            .WithMessage("La moneda indicada no está admitida.");
        RuleFor(r => r.TimeZone)
            .Must(tz => tz is null || TimeZones.IsValid(tz))
            .WithMessage("La zona horaria no es válida.");
    }
}

internal sealed class SaveRoleRequestValidator : AbstractValidator<SaveRoleRequest>
{
    public SaveRoleRequestValidator()
    {
        RuleFor(r => r.Name).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica el nombre del rol.")
            .Must(n => n.Trim().Length is >= 2 and <= Role.NameMaxLength)
            .WithMessage($"El nombre debe tener entre 2 y {Role.NameMaxLength} caracteres.");
        RuleFor(r => r.Description).MaximumLength(Role.DescriptionMaxLength)
            .WithMessage($"La descripción admite como máximo {Role.DescriptionMaxLength} caracteres.");
        RuleFor(r => r.Permissions).NotNull().WithMessage("Indica los permisos del rol.");
        RuleForEach(r => r.Permissions).Must(Permissions.IsKnown).WithMessage("El permiso '{PropertyValue}' no existe.");
    }
}

internal sealed class CreateInvitationRequestValidator : AbstractValidator<CreateInvitationRequest>
{
    public CreateInvitationRequestValidator()
    {
        RuleFor(r => r.Email).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica el correo electrónico.")
            .MaximumLength(254).WithMessage("El correo electrónico es demasiado largo.")
            .EmailAddress().WithMessage("El correo electrónico no es válido.");
        RuleFor(r => r.RoleId).NotEmpty().WithMessage("Elige un rol.");
    }
}

internal sealed class InvitationTokenRequestValidator : AbstractValidator<InvitationTokenRequest>
{
    public InvitationTokenRequestValidator()
    {
        RuleFor(r => r.TenantId).NotEmpty().WithMessage("El enlace de invitación no es válido.");
        RuleFor(r => r.Token).NotEmpty().MaximumLength(100).WithMessage("El enlace de invitación no es válido.");
    }
}

internal static class TimeZones
{
    public const string Default = "Europe/Madrid";

    /// <summary>IANA identifiers only (the runtime images ship ICU and tzdata).</summary>
    public static bool IsValid(string id) =>
        id.Contains('/', StringComparison.Ordinal) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);
}
