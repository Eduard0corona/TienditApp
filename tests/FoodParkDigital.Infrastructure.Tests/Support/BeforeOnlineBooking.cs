using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 24D added (appointments booked online and the owner's setting for them): the app as
/// deployed before it, and what a test needs to seed or read on a database migrated to an earlier migration, where those
/// columns do not exist yet.
/// </summary>
internal static class BeforeOnlineBooking
{
    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know them either.</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        var appointment = modelBuilder.Entity<Appointment>();
        // The account's key goes with its relationship, or it would stay as a shadow column.
        foreach (var key in appointment.Metadata.GetForeignKeys()
                     .Where(key => key.Properties.Any(property => property.Name == nameof(Appointment.CustomerUserId)))
                     .ToList())
            appointment.Metadata.RemoveForeignKey(key);
        appointment.Ignore(a => a.Channel);
        appointment.Ignore(a => a.CustomerUserId);
        appointment.Ignore(a => a.ClientRequestId);
        appointment.Ignore(a => a.TrackingToken);
        appointment.Ignore(a => a.CancelledByCustomerAt);
        modelBuilder.Entity<TenantSettings>().Ignore(s => s.OnlineBookingEnabled);
        modelBuilder.Entity<TenantSettings>().Ignore(s => s.OnlineBookingCapacity);
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
