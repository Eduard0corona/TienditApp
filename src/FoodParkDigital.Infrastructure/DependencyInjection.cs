using System.Text;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Identity;
using FoodParkDigital.Application.Identity.Services;
using FoodParkDigital.Application.OnlinePayments;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Infrastructure.BackgroundJobs;
using FoodParkDigital.Infrastructure.BackgroundJobs.Billing;
using FoodParkDigital.Infrastructure.BackgroundJobs.Inbox;
using FoodParkDigital.Infrastructure.BackgroundJobs.Messaging;
using FoodParkDigital.Infrastructure.BackgroundJobs.OnlinePayments;
using FoodParkDigital.Infrastructure.BackgroundJobs.Outbox;
using FoodParkDigital.Infrastructure.BackgroundJobs.Reporting;
using FoodParkDigital.Infrastructure.Identity;
using FoodParkDigital.Infrastructure.OnlinePayments;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Persistence.Interceptors;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace FoodParkDigital.Infrastructure;

public static class DependencyInjection
{
    // EF Core events that log the provider exception before rethrowing it. SQL Server copies row values
    // (duplicate keys, truncated text) into its messages, so outside Development these events are dropped
    // and whoever handles the exception logs it through LogRedaction.RedactDatabaseMessages.
    private static readonly EventId[] DatabaseErrorEvents =
    [
        CoreEventId.SaveChangesFailed,
        CoreEventId.QueryIterationFailed,
        RelationalEventId.ExecuteUpdateFailed,
        RelationalEventId.ExecuteDeleteFailed,
        RelationalEventId.NonQueryOperationFailed
    ];

    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Interceptors
        services.AddScoped<AuditableEntityInterceptor>();
        services.AddScoped<TenantSaveChangesInterceptor>();
        services.AddScoped<OutboxSaveChangesInterceptor>();

        // DbContext
        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            var auditInterceptor = sp.GetRequiredService<AuditableEntityInterceptor>();
            var tenantInterceptor = sp.GetRequiredService<TenantSaveChangesInterceptor>();
            var outboxInterceptor = sp.GetRequiredService<OutboxSaveChangesInterceptor>();
            var logSensitiveData = sp.GetRequiredService<IHostEnvironment>().IsDevelopment();

            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sqlOptions =>
                {
                    sqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                    sqlOptions.EnableRetryOnFailure(maxRetryCount: 3);
                    // A query that loads collections reads each one in its own statement: an order with its lines,
                    // payments, refunds and history in a single join would repeat the order row once per combination.
                    // A read may then see a change committed between two statements; what changes together (orders,
                    // stock, purchase lines, drawers) is saved against its concurrency token, so a stale copy fails.
                    sqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                });

            // The tenant interceptor validates rows before the outbox interceptor serializes their events.
            options.AddInterceptors(auditInterceptor, tenantInterceptor, outboxInterceptor);

            options.ConfigureWarnings(w =>
            {
                w.Ignore(RelationalEventId.PendingModelChangesWarning);
                // Logged on every save inside a transaction when the connection string enables MARS. Handled: a
                // transactional request never goes on after a failed save, it fails or discards its whole transaction
                // (data-consistency-and-domain-events.md).
                w.Ignore(SqlServerEventId.SavepointsDisabledBecauseOfMARS);
                if (!logSensitiveData)
                    w.Ignore(DatabaseErrorEvents);
            });
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        // Identity
        services.AddIdentity<ApplicationUser, ApplicationRole>(ConfigureIdentityOptions)
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddTienditAppIdentityDefaults();

        // JWT
        var jwtSettings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()!;
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidAudience = jwtSettings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
                ClockSkew = TimeSpan.Zero
            };

            // SignalR JWT support via query string
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                },
                // A genuine, unexpired token also needs its session open: signing out, a reused refresh token or a
                // change of the account's security end it before it expires.
                OnTokenValidated = async context =>
                {
                    var sessions = context.HttpContext.RequestServices.GetRequiredService<IAccessTokenSessionValidator>();
                    if (context.Principal is null
                        || !await sessions.IsOpenAsync(context.Principal, context.HttpContext.RequestAborted))
                    {
                        context.Fail("La sesión ya no es válida.");
                    }
                }
            };
        });

        services.AddAuthorization();

        // HTTP Clients. Sends are POSTs that are not idempotent at the provider: they get timeouts and a
        // circuit breaker but no immediate retry (a timed-out message may have been delivered). Background
        // senders retry later through the outbox, with backoff.
        services.AddHttpClient(WhatsAppCloudClient.HttpClientName).AddResilienceHandler("whatsapp", ConfigureProviderResilience);
        services.AddHttpClient("Twilio").AddResilienceHandler("twilio", ConfigureProviderResilience);
        services.AddHttpClient(MercadoPagoGateway.HttpClientName).AddResilienceHandler("mercadopago", ConfigureProviderResilience);

        // Background work: outbox (durable side effects of domain events), webhook inbox, messaging
        // (campaigns, reminders and the queue of outgoing WhatsApp messages), daily sales snapshots, billing status and
        // the sweep of online payments.
        services.Configure<BackgroundJobOptions>(configuration.GetSection(BackgroundJobOptions.SectionName));
        services.AddSingleton<OutboxSignal>();
        services.AddSingleton<InboxSignal>();
        services.AddSingleton<OutboxDispatcher>();
        // Events a request adds without an entity, such as the codes asked for without a session.
        services.AddScoped<IOutbox, OutboxWriter>();
        services.AddSingleton<InboxProcessor>();
        services.AddSingleton<MessagingSignal>();
        services.AddSingleton<IOutgoingMessageSignal>(sp => sp.GetRequiredService<MessagingSignal>());
        services.AddSingleton<MessagingWorker>();
        services.AddScoped<IWebhookInbox, WebhookInbox>();
        services.AddScoped<WhatsAppWebhookProcessor>();
        services.AddScoped<MercadoPagoWebhookProcessor>();
        services.AddScoped<IBackgroundExecutionContext, BackgroundExecutionContext>();
        // Data the previous version left without a business while the migration was applied, completed at start.
        services.AddHostedService<OrderTenantBackfillService>();
        if (configuration.GetValue($"{BackgroundJobOptions.SectionName}:Enabled", defaultValue: true))
        {
            services.AddHostedService<OutboxDispatcherService>();
            services.AddHostedService<InboxProcessorService>();
            services.AddHostedService<MessagingWorkerService>();
            services.AddHostedService<SalesSnapshotWorkerService>();
            services.AddHostedService<SubscriptionStatusWorkerService>();
            services.AddHostedService<OnlinePaymentsWorkerService>();
            services.AddHostedService<PrivateFilesMigrationService>();
        }

        // Services
        services.AddScoped<ICurrentTenantService, CurrentTenantService>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<ITenantAccessService, TenantAccessService>();
        services.AddScoped<IFolioGenerator, FolioGenerator>();
        services.AddScoped<IAppointmentBookingLock, AppointmentBookingLock>();
        services.AddScoped<IPhoneNormalizationService, PhoneNormalizationService>();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<ISignInService, SignInService>();
        services.AddScoped<ITwoFactorLoginChallengeService, TwoFactorLoginChallengeService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddSingleton<IAppLinks, AppLinks>();
        services.AddSingleton<WhatsAppCloudClient>();
        services.AddScoped<ISmsService, SmsService>();
        // Online payments (Fase 24I, ADR 0031): offered only when the MercadoPago section and the key are configured.
        services.Configure<MercadoPagoOptions>(configuration.GetSection(MercadoPagoOptions.SectionName));
        services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();
        services.AddSingleton<IOnlinePaymentGateway, MercadoPagoGateway>();
        services.AddScoped<IWhatsAppService, WhatsAppService>();
        services.AddSingleton<ITwoFactorService, TwoFactorService>();
        var fileStorageProvider = configuration["FileStorage:Provider"];
        if (string.Equals(fileStorageProvider, "AzureBlob", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IFileStorageService, AzureBlobFileStorageService>();
            services.AddSingleton<AzureBlobPrivateFileStorage>();
            services.AddSingleton<IPrivateFileStorage>(sp => sp.GetRequiredService<AzureBlobPrivateFileStorage>());
            services.AddSingleton<ILegacyPrivateFileMover>(sp => sp.GetRequiredService<AzureBlobPrivateFileStorage>());
        }
        else
        {
            services.AddScoped<IFileStorageService, LocalFileStorageService>();
            services.AddSingleton<LocalPrivateFileStorage>();
            services.AddSingleton<IPrivateFileStorage>(sp => sp.GetRequiredService<LocalPrivateFileStorage>());
            services.AddSingleton<ILegacyPrivateFileMover>(sp => sp.GetRequiredService<LocalPrivateFileStorage>());
        }

        services.AddSingleton<ImageProcessingService>();
        services.AddSingleton<IImageProcessingService>(sp => sp.GetRequiredService<ImageProcessingService>());
        services.AddSingleton<IImageNormalizationService>(sp => sp.GetRequiredService<ImageProcessingService>());
        services.AddHttpContextAccessor();

        return services;
    }

    private static void ConfigureProviderResilience(ResiliencePipelineBuilder<HttpResponseMessage> builder)
    {
        builder
            .AddTimeout(TimeSpan.FromSeconds(30))
            .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                // Stop calling a provider that keeps failing; callers get a transient failure and retry later.
                FailureRatio = 0.5,
                MinimumThroughput = 10,
                SamplingDuration = TimeSpan.FromSeconds(60),
                BreakDuration = TimeSpan.FromSeconds(30)
            })
            .AddTimeout(TimeSpan.FromSeconds(15));
    }

    public static void ConfigureIdentityOptions(IdentityOptions options)
    {
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = PasswordPolicy.MinimumLength;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.User.RequireUniqueEmail = true;
        // Per account, besides the per-IP limit of the auth endpoints: guessing a password or an SMS code from many
        // addresses locks the account for a while. Recovering the password unlocks it.
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = MaxFailedAccessAttempts;
        options.Lockout.DefaultLockoutTimeSpan = LockoutDuration;
    }

    public const int MaxFailedAccessAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
}
