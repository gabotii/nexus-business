using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.Application.Catalog;
using Nexus.Application.Customers;
using Nexus.Application.Tenancy;

namespace Nexus.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.TryAddSingleton(TimeProvider.System);

        // One tenant context per request/unit of work, shared by the EF filter, the write guard and RLS.
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        services.AddScoped<TenantAccessService>();
        services.AddScoped<TenantService>();
        services.AddScoped<TeamService>();
        services.AddScoped<InvitationService>();
        services.AddScoped<TenantSettingsService>();
        services.AddScoped<CustomerService>();
        services.AddScoped<TagService>();
        services.AddScoped<CatalogService>();
        return services;
    }
}
