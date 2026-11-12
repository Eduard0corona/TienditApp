using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 31I added (the days a person who takes online appointments is away, and their photo): the app
/// as deployed before it, and what a test needs on a database migrated to an earlier migration, where neither exists yet.
/// </summary>
internal static class BeforeStaffAbsencesAndPhotos
{
    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know them either.</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OnlineBookingStaff>().Ignore(staff => staff.Absences);
        modelBuilder.Entity<OnlineBookingStaff>().Ignore(staff => staff.PhotoUrl);
        modelBuilder.Ignore<OnlineBookingStaffAbsence>();
        BeforeAccountCarts.Ignore(modelBuilder);
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
