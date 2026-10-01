using System.Threading.RateLimiting;
using FoodParkDigital.Application;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Infrastructure;
using FoodParkDigital.Infrastructure.Identity;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Persistence.Seeds;
using FoodParkDigital.WebApi.Hubs;
using FoodParkDigital.WebApi.Middleware;
using FoodParkDigital.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Serilog;

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

// Serilog
builder.Host.UseSerilog((ctx, config) =>
    config.ReadFrom.Configuration(ctx.Configuration));

// Layer registrations
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// SignalR notification services
builder.Services.AddScoped<IOrderNotificationService, OrderNotificationService>();
builder.Services.AddScoped<IWhatsAppNotificationService, WhatsAppNotificationService>();

// Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        var message = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? $"{{\"error\": \"Demasiadas solicitudes. Intente de nuevo en {retryAfter.TotalSeconds} segundos.\"}}"
            : "{\"error\": \"Demasiadas solicitudes. Por favor, espere antes de intentar de nuevo.\"}";
        await context.HttpContext.Response.WriteAsync(message, token);
    };

    // Auth endpoints rate limit per IP (relaxed in Development for hot-reload)
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

// Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Swagger with JWT
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "FoodPark Digital API",
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
builder.Services.AddSignalR();

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
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        // User uploads are served from the API origin: they must never run as active content.
        if (!context.Context.Request.Path.StartsWithSegments("/uploads"))
            return;

        var headers = context.Context.Response.Headers;
        headers.ContentSecurityPolicy = "sandbox";
        if (context.File.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            headers.ContentDisposition = "attachment";
    }
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantResolutionMiddleware>();
app.MapControllers();
// Close hub connections when the access token expires so that reconnecting re-validates
// tenant membership and permissions.
app.MapHub<OrderHub>("/hubs/orders", options => options.CloseOnAuthenticationExpiration = true);
app.MapHub<ChatHub>("/hubs/chat", options => options.CloseOnAuthenticationExpiration = true);
app.MapFallback(async context =>
{
    var path = context.Request.Path;
    if (path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var webRootPath = app.Environment.WebRootPath;
    if (string.IsNullOrWhiteSpace(webRootPath))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var indexPath = Path.Combine(webRootPath, "index.html");
    if (!File.Exists(indexPath))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsync("Frontend artifact no encontrado. Asegura que client/dist se publique en wwwroot.");
        return;
    }

    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(indexPath);
});

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
