using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Nexus.Api.Web;
using Nexus.Application;
using Nexus.Application.Abstractions;
using Nexus.Infrastructure;
using Nexus.Infrastructure.Persistence;

// Container health probe (the runtime image has no curl): `dotnet Nexus.Api.dll --healthcheck`.
if (args.Contains("--healthcheck"))
{
    var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';')[0] ?? "8080";
    using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
    try
    {
        using var response = await probe.GetAsync(new Uri($"http://127.0.0.1:{port}/health/ready"));
        return response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (HttpRequestException)
    {
        return 1;
    }
    catch (TaskCanceledException)
    {
        return 1;
    }
}

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;
var environment = builder.Environment;

// Structured JSON logs outside development (one object per line, UTC, with scopes such as CorrelationId).
if (!environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(o =>
    {
        o.IncludeScopes = true;
        o.UseUtcTimestamp = true;
        o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    });
}

builder.WebHost.ConfigureKestrel(o =>
{
    o.AddServerHeader = false;
    // JSON API; the only uploads (company logo, Phase 3) are well below this.
    o.Limits.MaxRequestBodySize = 1_048_576;
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(configuration, environment);
builder.Services.AddNexusAuthentication();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<IRequestInfo, HttpRequestInfo>();

builder.Services.AddOptions<BrandingOptions>()
    .Bind(configuration.GetSection(BrandingOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<DatabaseOptions>()
    .Validate(o => !(environment.IsProduction() && o.MigrateOnStartup),
        "Database:MigrateOnStartup must be false in Production; run the 'migrate' command instead (ADR-0010).");

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)));

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = GlobalExceptionHandler.Customize);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// The default deployment serves SPA and API from one origin (nginx), so no origin is allowed unless configured.
var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowCredentials()
    .WithHeaders("Content-Type", "X-XSRF-TOKEN", "Idempotency-Key")
    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")));

builder.Services.AddNexusRateLimiting(configuration);

builder.Services.AddSwaggerGen(options =>
{
    var productName = configuration[$"{BrandingOptions.SectionName}:ProductName"] ?? new BrandingOptions().ProductName;
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = $"{productName} API",
        Version = "v1",
        Description = "API REST multiempresa. Los datos de empresa cuelgan de /api/v1/tenants/{tenantId}.",
    });
    var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml))
    {
        options.IncludeXmlComments(xml);
    }
});

if (configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
{
    // Only enable when the API is reachable exclusively through a trusted reverse proxy (see docker-compose.yml).
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

var app = builder.Build();

if (args.Contains("migrate"))
{
    return await MigrateCommand.RunAsync(app);
}

if (app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value is { MigrateOnStartup: true } database)
{
    await DatabaseMigrator.MigrateAsync(database.MigrationsConnectionString!,
        app.Services.GetRequiredService<ILogger<Program>>());
}

if (configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
{
    app.UseForwardedHeaders();
}

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    // Only emitted on HTTPS requests (directly or through X-Forwarded-Proto from the trusted proxy).
    app.UseHsts();
}

app.UseSecurityHeaders();

if (app.Environment.IsDevelopment() || configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.DocumentTitle = "API");
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgeryProtection();

app.MapControllers();
// Unknown routes answer 404 (as a problem, via status code pages) instead of the 401 of the deny-by-default policy.
app.MapFallback(() => Results.NotFound()).AllowAnonymous();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(HealthTags.Ready) })
    .AllowAnonymous();

await app.RunAsync();
return 0;

/// <summary>Entry point; partial so integration tests can reference it from WebApplicationFactory.</summary>
public partial class Program;
