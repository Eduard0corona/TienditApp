using System.Threading.RateLimiting;
using FoodParkDigital.Application;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Infrastructure;
using FoodParkDigital.Infrastructure.Identity;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Persistence.Seeds;
using FoodParkDigital.WebApi.Http;
using FoodParkDigital.WebApi.Hubs;
using FoodParkDigital.WebApi.Middleware;
using FoodParkDigital.WebApi.Observability;
using FoodParkDigital.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Serilog;
using FoodParkDigital.WebApi.Errors;

var builder = WebApplication.CreateBuilder(args);

// Local, git-ignored overrides (appsettings.{Environment}.local.json) for developer secrets.
builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.local.json",
    optional: true,
    reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);

// The JWT signing key must come from configuration outside the repository.
var jwtSecretError = JwtSecretPolicy.GetValidationError(builder.Configuration[$"{JwtSettings.SectionName}:Secret"]);
var usesEphemeralJwtSecret = false;
if (jwtSecretError is not null)
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            $"{jwtSecretError} Configura JwtSettings__Secret (mínimo {JwtSecretPolicy.MinimumSecretBytes} bytes aleatorios) " +
            "en las variables de entorno o el almacén de secretos del entorno.");
    }

    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        [$"{JwtSettings.SectionName}:Secret"] = JwtSecretPolicy.CreateEphemeralSecret()
    });
    usesEphemeralJwtSecret = true;
}

// Serilog writes the console and hands every event to the other logging providers: OpenTelemetry, when a
// destination is configured, so logs reach Application Insights or the collector with traces and metrics.
// The default providers go away so the console does not get every line twice.
builder.Logging.ClearProviders();
builder.Host.UseSerilog(
    (ctx, config) => config.ReadFrom.Configuration(ctx.Configuration).Enrich.FromLogContext(),
    writeToProviders: true);

// Layer registrations
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// Health checks and OpenTelemetry (exported only when a destination is configured)
builder.AddObservability();

// SignalR notification services
builder.Services.AddScoped<IOrderNotificationService, OrderNotificationService>();
builder.Services.AddScoped<IWhatsAppNotificationService, WhatsAppNotificationService>();

// WhatsApp webhook signature verification (fails closed without WhatsApp:AppSecret)
builder.Services.AddSingleton<WhatsAppWebhookSignatureVerifier>();

// Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, _) =>
    {
        var hasRetryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter);
        // A store's page is a page, not API: it still opens the app (Fase 24D-2).
        if (PublicPages.IsStorePage(context.HttpContext))
        {
            await PublicPages.WriteTooManyRequestsAsync(context.HttpContext, hasRetryAfter ? retryAfter : null);
            return;
        }

        var message = hasRetryAfter
            ? $"Demasiadas solicitudes. Intente de nuevo en {retryAfter.TotalSeconds} segundos."
            : "Demasiadas solicitudes. Por favor, espere antes de intentar de nuevo.";
        await ApiProblem.WriteAsync(context.HttpContext, StatusCodes.Status429TooManyRequests, message, ApiErrorCodes.TooManyRequests);
    };

    // Credential and code endpoints (login, registration, confirmation codes, password reset, 2FA codes),
    // per IP and relaxed in Development for hot-reload. Applied per action with [EnableRateLimiting("auth")].
    var isDev = builder.Environment.IsDevelopment();
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = isDev ? 100 : 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = isDev ? 10 : 2
            }));

    // Public menu (anonymous): browsing is generous, placing orders is tight, both per IP.
    options.AddPolicy("public-menu", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = isDev ? 600 : 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    // The public pages that read the database (a store, its booking page, the sitemap): as generous, in a bucket of their
    // own, so opening a store does not spend the limit of the menu it reads next.
    options.AddPolicy(PublicPages.RateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = isDev ? 600 : 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddPolicy("public-order", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = isDev ? 100 : 6,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0
            }));

    // Sliding window for general API endpoints - 200 requests per minute per user
    options.AddPolicy("api", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: httpContext.User?.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 200,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 4,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 10
            }));
});

// Controllers. Every error leaves as ProblemDetails with a stable code (see ApiErrorCodes), also the ones the
// framework writes: model binding, NotFound(), and empty 401/403/404 answers of /api (status code pages below).
// Binding errors answer in Spanish and as ProblemDetails, like the rest; the JSON reader's own messages stay out.
// Reads stop when their client leaves; changes finish once they arrived (RequestCancellation).
builder.Services.AddControllers(options =>
    {
        SpanishModelBinding.Apply(options.ModelBindingMessageProvider);
        RequestCancellation.Apply(options);
    })
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = SpanishModelBinding.InvalidModelStateResponse)
    .AddJsonOptions(options => options.AllowInputFormatterExceptionMessages = false);
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context => ApiProblem.Complete(context.ProblemDetails, context.HttpContext));
builder.Services.AddEndpointsApiExplorer();

// Swagger with JWT
builder.Services.AddSwaggerGen(options =>
{
    options.DocumentFilter<FoodParkDigital.WebApi.Swagger.LowercasePathsDocumentFilter>();
    // Accurate client types (client/src/api/generated): non-nullable properties are required.
    options.SupportNonNullableReferenceTypes();
    options.SchemaFilter<FoodParkDigital.WebApi.Swagger.RequireNonNullablePropertiesSchemaFilter>();
    // Named enum constants for the client (client/src/api/generated/enums.ts), generated from the contract.
    options.SchemaFilter<FoodParkDigital.WebApi.Swagger.EnumNamesSchemaFilter>();
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "TienditApp API",
        Version = "v1",
        Description = "Multi-tenant SaaS platform API for Food Park Digital"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// SignalR
// Live updates run in this process; with Azure:SignalR:ConnectionString they go through Azure SignalR Service,
// so several instances of the app share them (scale out) without a backplane of our own.
var signalR = builder.Services.AddSignalR();
if (!string.IsNullOrWhiteSpace(builder.Configuration["Azure:SignalR:ConnectionString"]))
    signalR.AddAzureSignalR();
// A live connection closes when its session ends or it loses access, not only when its token expires.
builder.Services.Configure<HubAccessOptions>(builder.Configuration.GetSection(HubAccessOptions.SectionName));
builder.Services.AddSingleton<HubConnections>();
builder.Services.AddHostedService<HubAccessSweeper>();

// CORS: the SPA is served by this API (and proxied by Vite in development), so cross-origin
// access is only granted to the origins listed in Cors:AllowedOrigins.
const string CorsPolicyName = "ConfiguredOrigins";
var allowedOrigins = (builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim().TrimEnd('/'))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy
                .WithOrigins(allowedOrigins)
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        }
    });
});

var app = builder.Build();

// Middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "FoodPark Digital API v1"));
}

app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api"),
    api => api.UseStatusCodePages());
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});
if (!app.Environment.IsDevelopment())
    app.UseHsts();
app.UseHttpsRedirection();
app.UseCors(CorsPolicyName);
app.UseMiddleware<PrivateUploadsGuardMiddleware>();
app.UseClientFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<RequestTelemetryEnrichmentMiddleware>();
app.MapHealthEndpoints();
app.MapControllers();
// Close hub connections when the access token expires so that reconnecting re-validates
// tenant membership and permissions.
app.MapHub<OrderHub>("/hubs/orders", options => options.CloseOnAuthenticationExpiration = true);
app.MapHub<ChatHub>("/hubs/chat", options => options.CloseOnAuthenticationExpiration = true);
// The store pages, robots.txt and sitemap.xml for search engines and link previews (Fase 24D-2, ADR 0027).
app.MapPublicPages();
app.MapClientFallback();

if (usesEphemeralJwtSecret)
{
    app.Logger.LogWarning(
        "{Reason} Se usa una llave efímera de desarrollo; los tokens dejarán de ser válidos al reiniciar la API.",
        jwtSecretError);
}

// Auto-migrate and seed only in Development, only when enabled, and never against a remote server
// unless explicitly allowed.
var databaseStartup = app.Configuration.GetSection(DatabaseStartupOptions.SectionName).Get<DatabaseStartupOptions>()
    ?? new DatabaseStartupOptions();
if (app.Environment.IsDevelopment() && (databaseStartup.AutoMigrateOnStartup || databaseStartup.SeedOnStartup))
{
    var connectionString = app.Configuration.GetConnectionString("DefaultConnection");
    if (!databaseStartup.AllowRemoteDatabase && !DatabaseStartupOptions.IsLocalDataSource(connectionString))
    {
        app.Logger.LogWarning(
            "Se omitieron la migración y el seed automáticos porque la base de datos no es local ({Host}). " +
            "Usa Database:AllowRemoteDatabase=true solo si es intencional.",
            DatabaseStartupOptions.GetHost(connectionString) ?? "desconocido");
    }
    else
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (databaseStartup.AutoMigrateOnStartup)
            await db.Database.MigrateAsync();
        if (databaseStartup.SeedOnStartup)
            await SeedData.SeedAsync(db, scope.ServiceProvider);
    }
}

await app.RunAsync();
