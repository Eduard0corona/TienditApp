using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 29A added (the visits to quote of the trades): the app as deployed before it, and what a
/// test needs to seed or read the settings of a business on a database migrated to an earlier migration, where the columns
/// do not exist yet.
/// </summary>
internal static class BeforeHomeVisitRequests
{
    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know it either.</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        var settings = modelBuilder.Entity<TenantSettings>();
        settings.Ignore(s => s.HomeVisitRequestsEnabled);
        settings.Ignore(s => s.HomeVisitServiceId);
        settings.Ignore(s => s.HomeVisitChargePolicy);
        settings.Ignore(s => s.HomeVisitAddressNoticeDismissedAt);
    }

    private sealed class Context(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentTenantService tenantService,
        IMediator mediator) : ApplicationDbContext(options, tenantService, mediator)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            Ignore(modelBuilder);
        }
    }
}
