using FoodParkDigital.Application.Common.Events;
using System.Reflection;
using FoodParkDigital.Application.Common.Behaviors;
using FoodParkDigital.Application.Identity.Services;
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
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly);
        services.AddAutoMapper(assembly);

        services.AddScoped<IAuthSessionIssuer, AuthSessionIssuer>();

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
