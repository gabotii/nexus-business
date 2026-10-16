using FluentValidation;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nexus.Application.Abstractions;
using Nexus.Infrastructure.Audit;
using Nexus.Infrastructure.Common;
using Nexus.Infrastructure.Email;
using Nexus.Infrastructure.Files;
using Nexus.Infrastructure.Identity;
using Nexus.Infrastructure.Persistence;

namespace Nexus.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<DatabaseOptions>()
            .Configure(options =>
            {
                options.ConnectionString = configuration.GetConnectionString("Default") ?? string.Empty;
                options.MigrationsConnectionString = configuration.GetConnectionString("Migrations");
                options.MigrateOnStartup = configuration.GetValue<bool>("Database:MigrateOnStartup");
            })
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString),
                $"ConnectionStrings:Default is required (it must authenticate as the '{DatabaseOptions.RuntimeRole}' role).")
            .Validate(o => !o.MigrateOnStartup || !string.IsNullOrWhiteSpace(o.MigrationsConnectionString),
                "Database:MigrateOnStartup requires ConnectionStrings:Migrations (schema owner).")
            .ValidateOnStart();

        services.AddScoped<RlsSessionInterceptor>();
        services.AddScoped<TenantWriteGuard>();
        services.AddDbContext<AppDbContext>((provider, builder) =>
        {
            DatabaseOptions.ConfigureProvider(builder, provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString);
            builder.AddInterceptors(provider.GetRequiredService<RlsSessionInterceptor>(),
                provider.GetRequiredService<TenantWriteGuard>());
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IDatabaseTools, DatabaseTools>();

        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: [HealthTags.Ready]);

        AddIdentity(services, configuration, environment);
        AddEmail(services, configuration, environment);

        services.AddOptions<AppOptions>()
            .Bind(configuration.GetSection(AppOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IAppLinks, AppLinks>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.RootPath), "Storage:RootPath is required.")
            .Validate(o => !environment.IsProduction() || Path.IsPathRooted(o.RootPath),
                "Storage:RootPath must be an absolute path (a persistent volume) in Production.")
            .ValidateOnStart();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.AddHostedService<HousekeepingService>();

        return services;
    }

    private static void AddIdentity(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services
            .AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                // Length is the only rule (NIST SP 800-63B); validated identically before any account lookup.
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Tokens.PasswordResetTokenProvider = PasswordResetTokenProvider.ProviderName;
            })
            .AddSignInManager()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<PasswordResetTokenProvider>(PasswordResetTokenProvider.ProviderName);

        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(24));
        // Other devices notice a security stamp change within this interval, on top of the explicit revocations.
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));

        // Session tickets and e-mail tokens are protected with these keys: they must survive restarts (and be shared
        // by every instance), so outside development they are persisted to a mounted folder.
        var dataProtection = services.AddDataProtection().SetApplicationName("nexus-business");
        var keysPath = configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        services.AddOptions<DataProtectionOptions>()
            .Validate(_ => !environment.IsProduction() || !string.IsNullOrWhiteSpace(keysPath),
                "DataProtection:KeysPath is required in Production (sessions would not survive a restart).")
            .ValidateOnStart();

        services.AddSingleton<DbTicketStore>();
        services.AddSingleton<ITicketStore>(sp => sp.GetRequiredService<DbTicketStore>());
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<SessionService>();
        services.AddScoped<AccountService>();
    }

    private static void AddEmail(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.Delivery != EmailDelivery.Smtp || !string.IsNullOrWhiteSpace(o.SmtpHost),
                "Email:SmtpHost is required when Email:Delivery is Smtp.")
            .Validate(o => !(environment.IsProduction() && o.Delivery == EmailDelivery.Pickup),
                "Email:Delivery=Pickup writes e-mails to disk and is not allowed in Production.")
            .ValidateOnStart();

        services.AddScoped<IEmailOutbox, EmailOutbox>();
        services.AddSingleton<IEmailTransport>(sp =>
            sp.GetRequiredService<IOptions<EmailOptions>>().Value.Delivery == EmailDelivery.Pickup
                ? ActivatorUtilities.CreateInstance<PickupEmailTransport>(sp)
                : ActivatorUtilities.CreateInstance<SmtpEmailTransport>(sp));
        services.AddSingleton<EmailDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<EmailDispatcher>());
    }
}
