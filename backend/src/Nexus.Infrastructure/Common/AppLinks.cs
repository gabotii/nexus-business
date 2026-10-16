using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Nexus.Application.Abstractions;

namespace Nexus.Infrastructure.Common;

public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>Public URL of the web app (scheme + host [+ port]), used to build links in e-mails.</summary>
    [Required]
    [Url]
    public string PublicBaseUrl { get; set; } = string.Empty;
}

internal sealed class AppLinks(IOptions<AppOptions> options, IConfiguration configuration) : IAppLinks
{
    private string Base => options.Value.PublicBaseUrl.TrimEnd('/');

    // Same key the API uses for its description and meta endpoint; the name is configuration, not code.
    public string ProductName => configuration["Branding:ProductName"] is { Length: > 0 } name ? name : "Nexus Business";

    public string VerifyEmail(Guid userId, string token) =>
        $"{Base}/verificar-correo?uid={userId}&token={Uri.EscapeDataString(token)}";

    public string ResetPassword(Guid userId, string token) =>
        $"{Base}/restablecer-contrasena?uid={userId}&token={Uri.EscapeDataString(token)}";

    public string Invitation(Guid tenantId, string token) =>
        $"{Base}/invitacion?empresa={tenantId}&token={Uri.EscapeDataString(token)}";
}
