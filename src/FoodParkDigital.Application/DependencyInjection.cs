using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using System.Globalization;
using System.Reflection;
using FoodParkDigital.Application.AiAgents;
using FoodParkDigital.Application.Appointments;
using FoodParkDigital.Application.Audit;
using FoodParkDigital.Application.Common.Behaviors;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Finance;
using FoodParkDigital.Application.Identity.Services;
using FoodParkDigital.Application.Inventory;
using FoodParkDigital.Application.Messaging;
using FoodParkDigital.Application.PublicDirectory;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            // Authorization first: a request the user may not send is not even validated.
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
            // Last, around the handler only; its constraints keep it to ITransactionalRequest commands.
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly);
        // The rules without their own message speak Spanish too, whatever the server's culture.
        ValidatorOptions.Global.LanguageManager.Culture = new CultureInfo("es");

        services.AddScoped<IAuthSessionIssuer, AuthSessionIssuer>();
        services.AddScoped<IAccessTokenSessionValidator, AccessTokenSessionValidator>();

        // Inventory: the ledger posts every movement; orders consume and return stock through it.
        services.AddScoped<InventoryLedger>();
        services.AddScoped<IOrderInventoryService, OrderInventoryService>();
        services.AddScoped<IOrderCashService, OrderCashService>();
        services.AddScoped<CurrentAccessContext>();
        services.AddScoped<ISupportAccessAudit, SupportAccessAudit>();

        // Messaging: used by the use cases and by the messaging worker, in the business's scope.
        services.AddScoped<MessagingConsentService>();
        services.AddScoped<CampaignAudienceResolver>();
        services.AddScoped<CampaignLauncher>();
        services.AddScoped<AppointmentReminderPlanner>();
        services.AddScoped<OutgoingMessageSender>();
        services.AddScoped<AppointmentNotices>();

        // Rules that used to live in Infrastructure: who an order's phone belongs to, and the automatic replies.
        services.AddScoped<ICustomerLookupService, CustomerLookupService>();
        services.AddScoped<ITemporaryCustomerClaimService, TemporaryCustomerClaimService>();
        services.AddScoped<IAiAgentAutoReplyService, AiAgentAutoReplyService>();

        // Explorar: one snapshot of the businesses it lists per instance, rebuilt at most every 30 seconds.
        services.AddSingleton<PublicDirectoryCache>();
        services.AddSingleton<PublicProductIndexCache>();

        // Durable handlers of domain events, run by the outbox dispatcher (one registration per event type).
        foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
        {
            foreach (var handlerInterface in type.GetInterfaces()
                         .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IOutboxEventHandler<>)))
            {
                services.AddScoped(handlerInterface, type);
            }
        }

        return services;
    }
}
