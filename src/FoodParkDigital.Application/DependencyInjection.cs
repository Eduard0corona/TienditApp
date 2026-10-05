using FoodParkDigital.Application.Common.Events;
using System.Reflection;
using FoodParkDigital.Application.Common.Behaviors;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Identity.Services;
using FoodParkDigital.Application.Messaging;
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

        services.AddScoped<IAuthSessionIssuer, AuthSessionIssuer>();
        services.AddScoped<CurrentAccessContext>();

        // Messaging: used by the use cases and by the messaging worker, in the business's scope.
        services.AddScoped<MessagingConsentService>();
        services.AddScoped<CampaignAudienceResolver>();
        services.AddScoped<CampaignLauncher>();
        services.AddScoped<AppointmentReminderPlanner>();
        services.AddScoped<OutgoingMessageSender>();

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
