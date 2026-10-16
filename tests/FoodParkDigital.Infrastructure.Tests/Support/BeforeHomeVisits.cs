using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 22E added (the travel time of the settings; the visit, the sale and the version of an
/// appointment; the visit charge of an order) nor what came after it (<see cref="BeforeBusinessDirectory"/>): the app as
/// deployed before it, and what a test needs to seed or read those tables on a database migrated to an earlier migration,
/// where those columns and tables do not exist yet.
/// </summary>
internal static class BeforeHomeVisits
{
    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know them either.</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Appointment>().Ignore(a => a.Visit);
        modelBuilder.Entity<Appointment>().Ignore(a => a.SaleOrderId);
        modelBuilder.Entity<Appointment>().Ignore(a => a.Version);
        modelBuilder.Entity<Order>().Ignore(o => o.VisitCharge);
        modelBuilder.Ignore<AppointmentVisit>();
        modelBuilder.Ignore<OrderVisitCharge>();
        modelBuilder.Entity<TenantSettings>().Ignore(s => s.HomeVisitTravelMinutes);
        BeforeBusinessDirectory.Ignore(modelBuilder);
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
