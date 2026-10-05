using FoodParkDigital.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Infrastructure.BackgroundJobs;

/// <summary>
/// The explicit business scope of work that runs outside a request. Workers and the webhook find their work across
/// businesses (the only reads without the tenant filter, each one in the bypass allowlist) and do each piece inside
/// the scope of its business: the query filters, the audit and the Application services then behave as in a
/// request of that business, with the job (or the user who caused it) as the actor.
/// </summary>
public static class BackgroundScopes
{
    /// <summary>A scope for the work of one business.</summary>
    public static AsyncServiceScope CreateTenantScope(
        this IServiceScopeFactory scopeFactory,
        Guid tenantId,
        string? actorName,
        string? actorUserId = null)
    {
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            scope.ServiceProvider.GetRequiredService<ICurrentTenantService>().SetTenant(tenantId);
            scope.ServiceProvider.GetRequiredService<IBackgroundExecutionContext>().SetActor(actorUserId, actorName);
            return scope;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    /// <summary>
    /// A scope that belongs to no business yet, to find the work (or the business it is for). The tenant filter shows
    /// no business's data in it.
    /// </summary>
    public static AsyncServiceScope CreateSystemScope(this IServiceScopeFactory scopeFactory, string actorName)
    {
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            scope.ServiceProvider.GetRequiredService<IBackgroundExecutionContext>().SetActor(null, actorName);
            return scope;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
