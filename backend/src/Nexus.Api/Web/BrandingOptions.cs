using System.ComponentModel.DataAnnotations;

namespace Nexus.Api.Web;

/// <summary>
/// The commercial name is configuration, not code: it appears in the API description, e-mails and the meta endpoint.
/// Business logic never depends on it.
/// </summary>
public sealed class BrandingOptions
{
    public const string SectionName = "Branding";

    [Required]
    [StringLength(60, MinimumLength = 1)]
    public string ProductName { get; set; } = "Nexus Business";
}
