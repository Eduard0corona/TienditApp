using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Entities;
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
        ApplyTenantRules(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyTenantRules(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyTenantRules(DbContext? context)
    {
        if (context is null) return;

        var currentTenantId = _tenantService.TenantId;

        // Tenants provisioned in this same unit of work (e.g. a SUPER_ADMIN creating a tenant with its
        // default customer and location) may receive rows while the request runs under another tenant.
        var tenantsBeingCreated = context.ChangeTracker.Entries<Tenant>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity.Id)
            .ToHashSet();

        foreach (var entry in context.ChangeTracker.Entries<ITenantScoped>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    StampOrValidateTenant(entry, currentTenantId, tenantsBeingCreated);
                    break;
                case EntityState.Modified when entry.Property(nameof(ITenantScoped.TenantId)).IsModified:
                    // TenantId is immutable once persisted.
                    throw new TenantMismatchException(entry.Metadata.ClrType.Name, GetId(entry));
            }
        }
    }

    private static void StampOrValidateTenant(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<ITenantScoped> entry,
        Guid currentTenantId,
        IReadOnlySet<Guid> tenantsBeingCreated)
    {
        // Without a tenant context (anonymous flows, webhooks, seed) the handler sets TenantId explicitly.
        if (currentTenantId == Guid.Empty)
            return;

        var tenantIdProperty = entry.Property(nameof(ITenantScoped.TenantId));
        var entityTenantId = (Guid)tenantIdProperty.CurrentValue!;

        if (entityTenantId == Guid.Empty)
        {
            tenantIdProperty.CurrentValue = currentTenantId;
            return;
        }

        if (entityTenantId != currentTenantId && !tenantsBeingCreated.Contains(entityTenantId))
            throw new TenantMismatchException(entry.Metadata.ClrType.Name, GetId(entry));
    }

    private static Guid GetId(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<ITenantScoped> entry) =>
        entry.Entity is BaseEntity entity ? entity.Id : Guid.Empty;
}
