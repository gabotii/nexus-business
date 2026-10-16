using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Nexus.Api.Web;

namespace Nexus.Api.Controllers;

/// <summary>Public, non-sensitive information about this deployment.</summary>
[ApiController]
[Route("api/v1/meta")]
[AllowAnonymous]
public sealed class MetaController(IOptions<BrandingOptions> branding) : ControllerBase
{
    private static readonly string Version = typeof(MetaController).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    /// <summary>Commercial name and API version, for the client shell and support.</summary>
    [HttpGet]
    public ActionResult<MetaResponse> Get() => new MetaResponse(branding.Value.ProductName, "v1", Version);
}

/// <param name="ProductName">Configured commercial name (<c>Branding:ProductName</c>).</param>
/// <param name="ApiVersion">Version of the HTTP contract.</param>
/// <param name="Version">Build version of the API.</param>
public sealed record MetaResponse(string ProductName, string ApiVersion, string Version);
