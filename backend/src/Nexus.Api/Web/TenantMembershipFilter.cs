using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Nexus.Application.Common;
using Nexus.Application.Tenancy;

namespace Nexus.Api.Web;

/// <summary>
/// Resolves <c>{tenantId}</c> from the route before model binding (ADR-0003): without an active membership in an active
/// company the request ends with 404 <c>tenant_not_found</c>, whatever the body or query string contain, and without
/// revealing whether the company exists.
/// </summary>
internal sealed class TenantMembershipFilter(TenantAccessService access) : IAsyncResourceFilter
{
    public const string RouteKey = "tenantId";

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var raw = context.RouteData.Values[RouteKey] as string;
        if (!Guid.TryParse(raw, out var tenantId) ||
            !await access.ResolveAsync(tenantId, context.HttpContext.RequestAborted))
        {
            throw new NotFoundException("La empresa no existe o no tienes acceso a ella.", "tenant_not_found");
        }

        await next();
    }
}

/// <summary>
/// Base for every controller under <c>/api/v1/tenants/{tenantId}</c>. Routes must start with
/// <see cref="Prefix"/>; the membership filter runs before anything else in the action pipeline.
/// </summary>
[ApiController]
[TypeFilter<TenantMembershipFilter>]
public abstract class TenantControllerBase : ControllerBase
{
    public const string Prefix = "api/v1/tenants/{tenantId:guid}";
}
