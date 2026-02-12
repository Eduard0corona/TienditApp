using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FoodParkDigital.Infrastructure.Persistence.Interceptors;

public class TenantSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentTenantService _tenantService;

    public TenantSaveChangesInterceptor(ICurrentTenantService tenantService)
    {
        _tenantService = tenantService;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        StampTenantId(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        StampTenantId(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void StampTenantId(DbContext? context)
    {
        if (context is null || _tenantService.TenantId == Guid.Empty) return;

        foreach (var entry in context.ChangeTracker.Entries<ITenantScoped>())
        {
            if (entry.State == EntityState.Added)
            {
                // Use reflection to set TenantId since it's read-only on the interface
                var tenantIdProperty = entry.Entity.GetType().GetProperty("TenantId");
                if (tenantIdProperty is not null && tenantIdProperty.CanWrite)
                {
                    var currentValue = (Guid)tenantIdProperty.GetValue(entry.Entity)!;
                    if (currentValue == Guid.Empty)
                    {
                        tenantIdProperty.SetValue(entry.Entity, _tenantService.TenantId);
                    }
                }
            }
        }
    }
}
