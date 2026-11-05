using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 29M added (whether a request online came from the business's own team, and the index Explorar
/// reads one request per customer by): the app as deployed before it, and what a test needs on a database migrated to an
/// earlier migration, where the column does not exist yet.
/// </summary>
internal static class BeforeAppointmentFromTeam
{
    private const string IndexName = "IX_Appointments_OnlineRequests_Customer";

    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know it either.</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        var appointment = modelBuilder.Entity<Appointment>();
        var index = appointment.Metadata.GetIndexes().SingleOrDefault(i => i.GetDatabaseName() == IndexName);
        if (index is not null)
            appointment.Metadata.RemoveIndex(index);
        appointment.Ignore(a => a.FromTeam);
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
