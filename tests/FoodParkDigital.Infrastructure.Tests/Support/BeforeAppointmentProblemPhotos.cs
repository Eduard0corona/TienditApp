using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 29H added (the photos of what happens a customer sends with a visit requested online): the
/// app as deployed before it, and what a test needs on a database migrated to an earlier migration, where the table does not
/// exist yet.
/// </summary>
internal static class BeforeAppointmentProblemPhotos
{
    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know it either.</summary>
    public static void Ignore(ModelBuilder modelBuilder) => modelBuilder.Ignore<AppointmentProblemPhoto>();

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
