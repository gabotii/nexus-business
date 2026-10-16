using FluentValidation;

namespace Nexus.Infrastructure.Identity;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);

public sealed record LoginRequest(string Email, string Password, bool RememberMe);

public sealed record VerifyEmailRequest(Guid UserId, string Token);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(Guid UserId, string Token, string NewPassword);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record UpdateProfileRequest(string DisplayName);

public sealed record MeDto(Guid Id, string Email, string DisplayName, bool EmailVerified);

public sealed record SessionDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt,
    string? UserAgent,
    string? IpPrefix,
    bool IsCurrent);

/// <summary>Length is the only composition rule (NIST SP 800-63B); the maximum bounds hashing cost.</summary>
internal static class PasswordRules
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Indica una contraseña.")
            .MinimumLength(MinLength).WithMessage($"La contraseña debe tener al menos {MinLength} caracteres.")
            .MaximumLength(MaxLength).WithMessage($"La contraseña admite como máximo {MaxLength} caracteres.");

    public static IRuleBuilderOptions<T, string> ValidEmail<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Indica tu correo electrónico.")
            .MaximumLength(254).WithMessage("El correo electrónico es demasiado largo.")
            .EmailAddress().WithMessage("El correo electrónico no es válido.");
}

internal sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(r => r.Email).ValidEmail();
        RuleFor(r => r.Password).StrongPassword();
        RuleFor(r => r.DisplayName).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica tu nombre.")
            .Must(n => n.Trim().Length is >= 2 and <= AppUser.DisplayNameMaxLength)
            .WithMessage($"El nombre debe tener entre 2 y {AppUser.DisplayNameMaxLength} caracteres.");
    }
}

internal sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(254).WithMessage("Indica tu correo electrónico.");
        RuleFor(r => r.Password).NotEmpty().MaximumLength(PasswordRules.MaxLength).WithMessage("Indica tu contraseña.");
    }
}

internal sealed class VerifyEmailRequestValidator : AbstractValidator<VerifyEmailRequest>
{
    public VerifyEmailRequestValidator()
    {
        RuleFor(r => r.UserId).NotEmpty().WithMessage("El enlace no es válido.");
        RuleFor(r => r.Token).NotEmpty().MaximumLength(2048).WithMessage("El enlace no es válido.");
    }
}

internal sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() => RuleFor(r => r.Email).ValidEmail();
}

internal sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(r => r.UserId).NotEmpty().WithMessage("El enlace no es válido.");
        RuleFor(r => r.Token).NotEmpty().MaximumLength(2048).WithMessage("El enlace no es válido.");
        RuleFor(r => r.NewPassword).StrongPassword();
    }
}

internal sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(r => r.CurrentPassword).NotEmpty().WithMessage("Indica tu contraseña actual.");
        RuleFor(r => r.NewPassword).StrongPassword()
            .NotEqual(r => r.CurrentPassword).WithMessage("La nueva contraseña debe ser distinta de la actual.");
    }
}

internal sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator() =>
        RuleFor(r => r.DisplayName).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica tu nombre.")
            .Must(n => n.Trim().Length is >= 2 and <= AppUser.DisplayNameMaxLength)
            .WithMessage($"El nombre debe tener entre 2 y {AppUser.DisplayNameMaxLength} caracteres.");
}
