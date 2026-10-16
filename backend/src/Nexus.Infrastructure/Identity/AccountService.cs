using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Nexus.Application.Abstractions;
using Nexus.Application.Common;
using Nexus.Domain.Common;
using Nexus.Infrastructure.Persistence;
using ValidationFailure = FluentValidation.Results.ValidationFailure;

namespace Nexus.Infrastructure.Identity;

/// <summary>
/// Account lifecycle on top of ASP.NET Core Identity (hashing, lockout, security stamps, tokens). Issuing and
/// clearing the session cookie happens in the API with <see cref="SignInManager{TUser}"/>.
/// Anti-enumeration (R4): registration and password recovery answer the same whether or not the e-mail has an
/// account, and failed sign-ins share one message and similar timing.
/// </summary>
public sealed class AccountService(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    AppDbContext db,
    SessionService sessions,
    IEmailOutbox outbox,
    IAppLinks links,
    IAuditLog audit,
    IValidator<RegisterRequest> registerValidator,
    IValidator<LoginRequest> loginValidator,
    IValidator<VerifyEmailRequest> verifyValidator,
    IValidator<ForgotPasswordRequest> forgotValidator,
    IValidator<ResetPasswordRequest> resetValidator,
    IValidator<ChangePasswordRequest> changeValidator,
    IValidator<UpdateProfileRequest> profileValidator,
    TimeProvider clock,
    ILogger<AccountService> logger)
{
    private const string InvalidCredentialsMessage =
        "Correo o contraseña incorrectos. Tras varios intentos fallidos la cuenta se bloquea durante 15 minutos.";

    private static readonly string DummyPasswordHash =
        new PasswordHasher<AppUser>().HashPassword(new AppUser(), "not-a-real-password-0000");

    /// <summary>
    /// Creates the account and e-mails a verification link. If the address already has an account, that account's
    /// owner is e-mailed instead (verification link again, or a "you already have an account" notice).
    /// </summary>
    public async Task RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        await registerValidator.ValidateAndThrowAsync(request, ct);
        var email = request.Email.Trim();

        var existing = await userManager.FindByEmailAsync(email);
        if (existing is null)
        {
            var user = new AppUser
            {
                Id = Guid.CreateVersion7(),
                UserName = email,
                Email = email,
                DisplayName = request.DisplayName.Trim(),
                CreatedAt = clock.GetUtcNow(),
            };

            var result = await userManager.CreateAsync(user, request.Password);
            if (result.Succeeded)
            {
                await QueueVerificationAsync(user);
                audit.Account("auth.registered", user.Id);
                await db.SaveChangesAsync(ct);
                logger.LogInformation("User {UserId} registered", user.Id);
                return;
            }

            if (!result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
            {
                throw new ValidationException(result.Errors.Select(e => new ValidationFailure("password", e.Description)));
            }

            // Lost a race with a concurrent registration of the same address: treat it as an existing account.
            existing = await userManager.FindByEmailAsync(email);
            if (existing is null)
            {
                return;
            }
        }
        else
        {
            // Same work as hashing a new password, so response times do not reveal the account.
            new PasswordHasher<AppUser>().VerifyHashedPassword(new AppUser(), DummyPasswordHash, request.Password);
        }

        if (existing.EmailConfirmed)
        {
            outbox.Enqueue(existing.Email!, $"Ya tienes una cuenta en {links.ProductName}",
                $"Hola, {existing.DisplayName}:\n\nAlguien (quizá tú) ha intentado registrarse en {links.ProductName} " +
                "con este correo, pero ya tienes una cuenta. Si no recuerdas tu contraseña, usa la opción " +
                "«¿Olvidaste tu contraseña?» en la pantalla de inicio de sesión.\n\nSi no has sido tú, ignora este mensaje.");
        }
        else
        {
            await QueueVerificationAsync(existing);
        }

        audit.Account("auth.register_existing", existing.Id);
        await db.SaveChangesAsync(ct);
    }

    public async Task VerifyEmailAsync(VerifyEmailRequest request, CancellationToken ct)
    {
        await verifyValidator.ValidateAndThrowAsync(request, ct);
        var user = await userManager.FindByIdAsync(request.UserId.ToString()) ?? throw InvalidLink();
        if (user.EmailConfirmed)
        {
            return;
        }

        var result = await userManager.ConfirmEmailAsync(user, DecodeToken(request.Token));
        if (!result.Succeeded)
        {
            throw InvalidLink();
        }

        audit.Account("auth.email_verified", user.Id);
        await db.SaveChangesAsync(ct);
    }

    public async Task ResendVerificationAsync(Guid userId, CancellationToken ct)
    {
        var user = await GetUserAsync(userId);
        if (user.EmailConfirmed)
        {
            return;
        }

        await QueueVerificationAsync(user);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Checks credentials with lockout (5 failures → 15 minutes). Every failure gets the same answer.</summary>
    public async Task<AppUser> AuthenticateAsync(LoginRequest request, CancellationToken ct)
    {
        await loginValidator.ValidateAndThrowAsync(request, ct);
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            new PasswordHasher<AppUser>().VerifyHashedPassword(new AppUser(), DummyPasswordHash, request.Password);
            audit.Account("auth.login_failed", userId: null);
            await db.SaveChangesAsync(ct);
            throw new UnauthenticatedException("invalid_credentials", InvalidCredentialsMessage);
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            audit.Account(result.IsLockedOut ? "auth.login_locked_out" : "auth.login_failed", user.Id);
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Failed sign-in for user {UserId} (locked out: {LockedOut})", user.Id, result.IsLockedOut);
            throw new UnauthenticatedException("invalid_credentials", InvalidCredentialsMessage);
        }

        audit.Account("auth.login_succeeded", user.Id);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<AppUser> GetUserAsync(Guid userId) =>
        await userManager.FindByIdAsync(userId.ToString())
        ?? throw new UnauthenticatedException("unauthorized", "Tu sesión ya no es válida. Inicia sesión de nuevo.");

    public static MeDto ToMe(AppUser user) => new(user.Id, user.Email!, user.DisplayName, user.EmailConfirmed);

    public async Task<MeDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct)
    {
        await profileValidator.ValidateAndThrowAsync(request, ct);
        var user = await GetUserAsync(userId);
        user.DisplayName = request.DisplayName.Trim();
        await userManager.UpdateAsync(user);
        return ToMe(user);
    }

    /// <summary>E-mails a reset link (valid for 1 hour) if the account exists; the answer never says whether it does.</summary>
    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct)
    {
        await forgotValidator.ValidateAndThrowAsync(request, ct);
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return;
        }

        var token = EncodeToken(await userManager.GeneratePasswordResetTokenAsync(user));
        outbox.Enqueue(user.Email!, $"Restablece tu contraseña de {links.ProductName}",
            $"Hola, {user.DisplayName}:\n\nPara elegir una nueva contraseña abre este enlace (caduca en 1 hora):\n" +
            $"{links.ResetPassword(user.Id, token)}\n\n" +
            "Si no lo has pedido tú, ignora este mensaje: tu contraseña no cambiará.");
        audit.Account("auth.password_reset_requested", user.Id);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Sets a new password from an e-mailed link and signs out every session of the account.</summary>
    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        await resetValidator.ValidateAndThrowAsync(request, ct);
        var user = await userManager.FindByIdAsync(request.UserId.ToString()) ?? throw InvalidLink();
        var result = await userManager.ResetPasswordAsync(user, DecodeToken(request.Token), request.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == "InvalidToken"))
            {
                throw InvalidLink();
            }

            throw new ValidationException(result.Errors.Select(e => new ValidationFailure("newPassword", e.Description)));
        }

        // The link proved control of the mailbox.
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await userManager.UpdateAsync(user);
        }

        await userManager.ResetAccessFailedCountAsync(user);
        await userManager.SetLockoutEndDateAsync(user, null);
        var revoked = await sessions.RevokeAllAsync(user.Id, ct);
        audit.Account("auth.password_reset", user.Id, new { RevokedSessions = revoked });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("User {UserId} reset their password", user.Id);
    }

    /// <summary>
    /// Changes the password and signs out every other session. Identity rotates the security stamp; the API
    /// refreshes the current device's ticket.
    /// </summary>
    public async Task<AppUser> ChangePasswordAsync(Guid userId, Guid? currentSessionId, ChangePasswordRequest request,
        CancellationToken ct)
    {
        await changeValidator.ValidateAndThrowAsync(request, ct);
        var user = await GetUserAsync(userId);
        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == "PasswordMismatch"))
            {
                throw new ValidationException(
                    [new ValidationFailure("currentPassword", "La contraseña actual no es correcta.")]);
            }

            throw new ValidationException(result.Errors.Select(e => new ValidationFailure("newPassword", e.Description)));
        }

        var revoked = await sessions.RevokeOthersAsync(user.Id, currentSessionId, ct);
        audit.Account("auth.password_changed", user.Id, new { RevokedSessions = revoked });
        await db.SaveChangesAsync(ct);
        return user;
    }

    private async Task QueueVerificationAsync(AppUser user)
    {
        var token = EncodeToken(await userManager.GenerateEmailConfirmationTokenAsync(user));
        outbox.Enqueue(user.Email!, $"Confirma tu correo en {links.ProductName}",
            $"Hola, {user.DisplayName}:\n\nPara confirmar tu dirección de correo abre este enlace (caduca en 24 horas):\n" +
            $"{links.VerifyEmail(user.Id, token)}\n\n" +
            "Si no has creado una cuenta, ignora este mensaje.");
    }

    private static string EncodeToken(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private static string DecodeToken(string token)
    {
        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
        }
        catch (FormatException)
        {
            throw InvalidLink();
        }
    }

    private static DomainException InvalidLink() =>
        new("invalid_link", "El enlace no es válido o ha caducado. Solicita uno nuevo.");
}
