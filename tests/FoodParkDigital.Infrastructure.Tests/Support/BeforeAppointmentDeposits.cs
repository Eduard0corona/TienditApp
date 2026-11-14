using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 31O added (the share of a deposit a business asks to book online, the deposit an appointment
/// was booked with and where it stands, and the checkout that books an appointment): the app as deployed before it, and what
/// a test needs on a database migrated to an earlier migration, where its columns do not exist yet.
/// </summary>
internal static class BeforeAppointmentDeposits
{
    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know them either.</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantSettings>().Ignore(settings => settings.BookingDepositPercent);
        var appointment = modelBuilder.Entity<Appointment>();
        appointment.Ignore(a => a.DepositAmount);
        appointment.Ignore(a => a.DepositStatus);
        // The appointment a checkout booked: its key to the appointments and its index go with it.
        var checkout = modelBuilder.Entity<OnlineCheckout>();
        foreach (var foreignKey in checkout.Metadata.GetForeignKeys()
                     .Where(k => k.Properties.Any(p => p.Name == nameof(OnlineCheckout.AppointmentId)))
                     .ToList())
            checkout.Metadata.RemoveForeignKey(foreignKey);
        foreach (var index in checkout.Metadata.GetIndexes()
                     .Where(i => i.Properties.Any(p => p.Name == nameof(OnlineCheckout.AppointmentId)))
                     .ToList())
            checkout.Metadata.RemoveIndex(index);
        checkout.Ignore(c => c.AppointmentId);
        checkout.Ignore(c => c.ForAppointment);
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
