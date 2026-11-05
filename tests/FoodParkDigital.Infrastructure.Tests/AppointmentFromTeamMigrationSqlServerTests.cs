using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// AddAppointmentFromTeam (Fase 29M, ADR 0067): whether a request online came from the business's own team. The appointments
/// that exist are not, the version before it keeps taking requests (saved as not from the team), and rolling back drops only
/// the column and its index, never an appointment.
/// </summary>
public class AppointmentFromTeamMigrationSqlServerTests
{
    // The migration right before AddAppointmentFromTeam, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261103170924_AddAppointmentRespondedAt";
    private const string Migration = "20261105081150_AddAppointmentFromTeam";
    private const string IndexName = "IX_Appointments_OnlineRequests_Customer";
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 15, 0, 0, TimeSpan.Zero);
    private static readonly DeliveryAddress Address = DeliveryAddress.ForVisit("72000", "Av. Juárez 123, Col. Centro", null);

    [SqlServerFact]
    public async Task TheAppointmentsThatExist_ShouldNotBeFromTheTeam_AndTheRequestsOnlineAreIndexedByCustomer()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        Guid tenantId;
        var (tenant, settings, zone) = Business();
        await using (var before = BeforeAppointmentFromTeam.CreateContext(database))
        {
            var answered = Request(tenant.Id, zone);
            var ofTheTeam = new Appointment(tenant.Id, "Ana", "5522107765", "Revisión", Start, Start.AddHours(1));
            before.AddRange(tenant, settings, zone, answered, ofTheTeam);
            await before.SaveChangesAsync();
            answered.Accept(Now);
            await before.SaveChangesAsync();
            tenantId = tenant.Id;
        }

        await MigrateAsync(database, Migration);

        await using var context = database.CreateContext(tenantId);
        var appointments = await context.Appointments.AsNoTracking().ToListAsync();
        Assert.Equal(2, appointments.Count);
        Assert.All(appointments, a => Assert.False(a.FromTeam));
        Assert.Contains(appointments, a => a is { Status: AppointmentStatus.Scheduled, RespondedAt: not null });
        Assert.Equal("([Channel]=(1))", await IndexFilterAsync(context));

        // A request from now on keeps whether it came from the team.
        var fromTeam = Request(tenantId, zone, fromTeam: true);
        context.Appointments.Add(fromTeam);
        await context.SaveChangesAsync();
        await using var check = database.CreateContext(tenantId);
        Assert.True((await check.Appointments.AsNoTracking().SingleAsync(a => a.Id == fromTeam.Id)).FromTeam);
    }

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepTakingRequests_NotFromTheTeam()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);

        // The app as deployed before this version takes a request and declines it, unaware of the column.
        Guid tenantId;
        Guid appointmentId;
        await using (var before = BeforeAppointmentFromTeam.CreateContext(database))
        {
            var (tenant, settings, zone) = Business();
            var request = Request(tenant.Id, zone);
            before.AddRange(tenant, settings, zone, request);
            await before.SaveChangesAsync();
            request.Decline(Now);
            await before.SaveChangesAsync();
            (tenantId, appointmentId) = (tenant.Id, request.Id);
        }

        await using var context = database.CreateContext(tenantId);
        var declined = await context.Appointments.AsNoTracking().SingleAsync(a => a.Id == appointmentId);
        Assert.Equal((AppointmentStatus.Cancelled, (DateTimeOffset?)Now, false), (declined.Status, declined.RespondedAt, declined.FromTeam));
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldDropOnlyTheColumnAndItsIndex_AndForwardAgainItKeepsWhoMadeIt()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var (tenant, settings, zone) = Business();
        var request = Request(tenant.Id, zone, fromTeam: true);
        await using (var seed = database.CreateContext(tenant.Id))
        {
            seed.AddRange(tenant, settings, zone, request);
            await seed.SaveChangesAsync();
            request.Accept(Now);
            await seed.SaveChangesAsync();
        }

        await MigrateAsync(database, BeforeMigration);
        await using (var context = BeforeAppointmentFromTeam.CreateContext(database, tenant.Id))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Null(await context.Database.SqlQuery<int?>($"SELECT COL_LENGTH(N'dbo.Appointments', N'FromTeam') AS [Value]").SingleAsync());
            Assert.Null(await IndexFilterAsync(context));
            // The appointment stays as it was, its answer too.
            var accepted = await context.Appointments.AsNoTracking().SingleAsync(a => a.Id == request.Id);
            Assert.Equal((AppointmentStatus.Scheduled, (DateTimeOffset?)Now), (accepted.Status, accepted.RespondedAt));
        }

        // Rolling back again does nothing, and forward again a request keeps whether it came from the team.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(tenant.Id);
        var another = Request(tenant.Id, zone, fromTeam: true);
        forward.Appointments.Add(another);
        await forward.SaveChangesAsync();
        Assert.Equal(
            [(request.Id, false), (another.Id, true)],
            (await forward.Appointments.AsNoTracking().ToListAsync())
                .OrderBy(a => a.FromTeam)
                .Select(a => (a.Id, a.FromTeam)));
    }

    private static (Tenant Tenant, TenantSettings Settings, DeliveryZone Zone) Business()
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Plomería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
        var settings = new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments);
        var zone = new DeliveryZone(tenant.Id, new DeliveryZoneFields("Centro", 50m, 0m, null, ["72000"]));
        return (tenant, settings, zone);
    }

    private static Appointment Request(Guid tenantId, DeliveryZone zone, bool fromTeam = false)
        => Appointment.RequestHomeVisit(
            tenantId, "Valeria", "+525522107765", null, HomeVisitRules.DefaultVisitName, Start, Start.AddHours(1),
            "Gotea el calentador", null, Address, zone, HomeVisitChargePolicy.Charged, null, Guid.NewGuid(),
            // Each request its own link, as the booking page makes them.
            (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))[..43], fromTeam: fromTeam);

    private static Task<string?> IndexFilterAsync(ApplicationDbContext context)
        => context.Database.SqlQuery<string?>($"""
            SELECT [filter_definition] AS [Value] FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'dbo.Appointments') AND [name] = {IndexName}
            """).SingleOrDefaultAsync();

    private static async Task MigrateAsync(SqlServerTestDatabase database, string target)
    {
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static Task<string> LatestMigrationAsync(ApplicationDbContext context) =>
        context.Database.SqlQuery<string>($"""
            SELECT TOP (1) [MigrationId] AS [Value] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC
            """).SingleAsync();
}
