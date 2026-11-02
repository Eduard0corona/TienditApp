using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 29F added (the service order whose job an appointment does): the app as deployed before it,
/// and what a test needs to seed or read appointments on a database migrated to an earlier migration, where the column does
/// not exist yet.
/// </summary>
internal static class BeforeAppointmentWork
{
    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know it either.</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        // The relationship to the order first: left alone, it would keep WorkOrderId as a shadow property.
        var appointments = modelBuilder.Entity<Appointment>();
        foreach (var foreignKey in appointments.Metadata.GetForeignKeys()
                     .Where(k => k.Properties.Any(p => p.Name == nameof(Appointment.WorkOrderId))).ToList())
            appointments.Metadata.RemoveForeignKey(foreignKey);
        appointments.Ignore(a => a.WorkOrderId);
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
