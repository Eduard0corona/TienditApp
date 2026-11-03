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
/// AddAppointmentRespondedAt (Fase 29J, ADR 0064): when the business answered a request online, and the time its customer asked
/// for. The appointments that exist keep neither, the version before it keeps taking and answering requests (without keeping
/// them), and rolling back drops only the columns and their index, never an appointment.
/// </summary>
public class AppointmentRespondedAtMigrationSqlServerTests
{
    // The migration right before AddAppointmentRespondedAt, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261102205836_AddAppointmentProblemPhotos";
    private const string Migration = "20261103170924_AddAppointmentRespondedAt";
    private const string IndexName = "IX_Appointments_OnlineRequests_CreatedAt";
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 15, 0, 0, TimeSpan.Zero);
    private static readonly DeliveryAddress Address = DeliveryAddress.ForVisit("72000", "Av. Juárez 123, Col. Centro", null);

    [SqlServerFact]
    public async Task TheAppointmentsThatExist_ShouldKeepNoAnswerTime_AndTheRequestsOnlineAreIndexed()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        Guid tenantId;
        Guid answeredId;
        Guid ofTheTeamId;
        var (tenant, settings, zone) = Business();
        await using (var before = BeforeAppointmentRespondedAt.CreateContext(database))
        {
            var answered = Request(tenant.Id, zone);
            var ofTheTeam = new Appointment(tenant.Id, "Ana", "5522107765", "Revisión", Start, Start.AddHours(1));
            before.AddRange(tenant, settings, zone, answered, ofTheTeam);
            await before.SaveChangesAsync();
            answered.Accept(Now);
            await before.SaveChangesAsync();
            (tenantId, answeredId, ofTheTeamId) = (tenant.Id, answered.Id, ofTheTeam.Id);
        }

        await MigrateAsync(database, Migration);

        await using var context = database.CreateContext(tenantId);
        var appointments = await context.Appointments.AsNoTracking().OrderBy(a => a.Channel).ToListAsync();
        Assert.Equal([(ofTheTeamId, (DateTimeOffset?)null), (answeredId, null)], appointments.Select(a => (a.Id, a.RespondedAt)));
        Assert.All(appointments, a => Assert.Null(a.RequestedStartAt));
        Assert.Equal(AppointmentStatus.Scheduled, appointments.Single(a => a.Id == answeredId).Status);
        Assert.Equal("([Channel]=(1))", await IndexFilterAsync(context));

        // A request answered from now on keeps when, to the tick.
        var request = Request(tenantId, zone);
        context.Appointments.Add(request);
        await context.SaveChangesAsync();
        var answeredAt = Now.AddMinutes(37).AddTicks(1234567);
        request.Decline(answeredAt);
        await context.SaveChangesAsync();
        await using var check = database.CreateContext(tenantId);
        var kept = await check.Appointments.AsNoTracking().SingleAsync(a => a.Id == request.Id);
        Assert.Equal((answeredAt, (DateTimeOffset?)Start), (kept.RespondedAt, kept.RequestedStartAt));
    }

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepTakingAndAnsweringRequests()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);

        // The app as deployed before this version takes a request and declines it, unaware of the answer time.
        Guid tenantId;
        Guid appointmentId;
        await using (var before = BeforeAppointmentRespondedAt.CreateContext(database))
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
        Assert.Equal((AppointmentStatus.Cancelled, (DateTimeOffset?)null), (declined.Status, declined.RespondedAt));
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldDropOnlyTheColumnAndItsIndex_AndForwardAgainItKeepsAnswers()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var (tenant, settings, zone) = Business();
        var request = Request(tenant.Id, zone);
        await using (var seed = database.CreateContext(tenant.Id))
        {
            seed.AddRange(tenant, settings, zone, request);
            await seed.SaveChangesAsync();
            request.Accept(Now);
            await seed.SaveChangesAsync();
        }

        await MigrateAsync(database, BeforeMigration);
        await using (var context = BeforeAppointmentRespondedAt.CreateContext(database, tenant.Id))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Null(await context.Database.SqlQuery<int?>($"SELECT COL_LENGTH(N'dbo.Appointments', N'RespondedAt') AS [Value]").SingleAsync());
            Assert.Null(await context.Database.SqlQuery<int?>($"SELECT COL_LENGTH(N'dbo.Appointments', N'RequestedStartAt') AS [Value]").SingleAsync());
            Assert.Null(await IndexFilterAsync(context));
            // The appointment stays as it was.
            var accepted = await context.Appointments.AsNoTracking().SingleAsync(a => a.Id == request.Id);
            Assert.Equal((AppointmentStatus.Scheduled, "Gotea el calentador"), (accepted.Status, accepted.Notes));
        }

        // Rolling back again does nothing, and forward again a request keeps when it was answered.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(tenant.Id);
        var another = Request(tenant.Id, zone);
        forward.Appointments.Add(another);
        await forward.SaveChangesAsync();
        another.Accept(Now);
        await forward.SaveChangesAsync();
        Assert.Equal(
            [(request.Id, (DateTimeOffset?)null), (another.Id, Now)],
            (await forward.Appointments.AsNoTracking().ToListAsync())
                .OrderBy(a => a.RespondedAt is null ? 0 : 1)
                .Select(a => (a.Id, a.RespondedAt)));
    }

    private static (Tenant Tenant, TenantSettings Settings, DeliveryZone Zone) Business()
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Plomería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
        var settings = new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments);
        var zone = new DeliveryZone(tenant.Id, new DeliveryZoneFields("Centro", 50m, 0m, null, ["72000"]));
        return (tenant, settings, zone);
    }

    private static Appointment Request(Guid tenantId, DeliveryZone zone)
        => Appointment.RequestHomeVisit(
            tenantId, "Valeria", "+525522107765", null, HomeVisitRules.DefaultVisitName, Start, Start.AddHours(1),
            "Gotea el calentador", null, Address, zone, HomeVisitChargePolicy.Charged, null, Guid.NewGuid(),
            // Each request its own link, as the booking page makes them.
            (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))[..43]);

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
