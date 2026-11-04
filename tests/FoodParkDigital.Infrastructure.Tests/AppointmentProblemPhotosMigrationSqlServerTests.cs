using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// AddAppointmentProblemPhotos (Fase 29H, ADR 0062): the photos of what happens a customer sends with a visit requested online.
/// The version before it keeps taking and deciding requests; the database keeps each place of a visit once and none past the
/// fifth, and the photos go with their appointment; rolling back stops with error 50056 once a customer sent a photo, and
/// otherwise drops only the table. The database at AddAppointmentProblemPhotos is read with the model of its day, without the
/// answer time of Fase 29J.
/// </summary>
public class AppointmentProblemPhotosMigrationSqlServerTests
{
    // The migration right before AddAppointmentProblemPhotos, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261102151555_AddQuoteOnlineReplies";
    private const string Migration = "20261102205836_AddAppointmentProblemPhotos";
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 21, 0, 0, TimeSpan.Zero);
    private static readonly DeliveryAddress Address = DeliveryAddress.ForVisit("72000", "Av. Juárez 123, Col. Centro", null);

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepTakingAndDecidingVisitRequests()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);

        // The app as deployed before this version takes a request and accepts it, unaware of the photos.
        Guid tenantId;
        Guid appointmentId;
        await using (var before = BeforeAppointmentProblemPhotos.CreateContext(database))
        {
            var (tenant, settings, zone) = Business();
            var request = Request(tenant.Id, zone);
            before.AddRange(tenant, settings, zone, request);
            await before.SaveChangesAsync();
            request.Accept(Now);
            await before.SaveChangesAsync();
            (tenantId, appointmentId) = (tenant.Id, request.Id);
        }

        await using var context = BeforeAppointmentRespondedAt.CreateContext(database, tenantId);
        Assert.Equal(AppointmentStatus.Scheduled, (await context.Appointments.SingleAsync(a => a.Id == appointmentId)).Status);
        Assert.Equal(0, await context.AppointmentProblemPhotos.CountAsync());
    }

    [SqlServerFact]
    public async Task TheDatabase_ShouldKeepEachPlaceOnce_NonePastTheFifth_AndThePhotosGoWithTheirAppointment()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var (tenantId, appointmentId) = await SeedAsync(database, photos: 1);

        await using var check = BeforeAppointmentRespondedAt.CreateContext(database, tenantId);
        var photo = await check.AppointmentProblemPhotos.AsNoTracking().SingleAsync();
        Assert.Equal((appointmentId, 0), (photo.AppointmentId, photo.Position));

        // Two photos sent at the same time, with the same count read before saving: only one takes the place.
        var samePlace = await Record.ExceptionAsync(() => InsertAsync(check, tenantId, appointmentId, 0));
        Assert.Contains("IX_AppointmentProblemPhotos_AppointmentId_Position", samePlace?.Message);
        var sixth = await Record.ExceptionAsync(() => InsertAsync(check, tenantId, appointmentId, HomeVisitRules.MaxProblemPhotos));
        Assert.Contains("CK_AppointmentProblemPhotos_Position", sixth?.Message);
        // The same file in two photos of the business.
        var sameFile = await Record.ExceptionAsync(() => InsertAsync(check, tenantId, appointmentId, 1, photo.FileKey));
        Assert.Contains("IX_AppointmentProblemPhotos_TenantId_FileKey", sameFile?.Message);

        await check.Database.ExecuteSqlRawAsync($"DELETE FROM [AppointmentVisits] WHERE [AppointmentId] = '{appointmentId}'");
        await check.Database.ExecuteSqlRawAsync($"DELETE FROM [Appointments] WHERE [Id] = '{appointmentId}'");
        Assert.Equal(0, await check.AppointmentProblemPhotos.IgnoreQueryFilters().CountAsync());
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceAPhotoWasSent_AndThenDropOnlyTheTable()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var (tenantId, appointmentId) = await SeedAsync(database, photos: 2);

        var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeMigration));
        Assert.True(stopped is SqlException { Number: 50056 }, stopped?.Message ?? "it was not stopped");
        await using (var context = BeforeAppointmentRespondedAt.CreateContext(database, tenantId))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Equal(2, await context.AppointmentProblemPhotos.CountAsync());
            await context.Database.ExecuteSqlRawAsync("DELETE FROM [AppointmentProblemPhotos]");
        }

        await MigrateAsync(database, BeforeMigration);
        await using (var context = BeforeAppointmentProblemPhotos.CreateContext(database, tenantId))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Null(await context.Database.SqlQuery<int?>($"SELECT OBJECT_ID(N'dbo.AppointmentProblemPhotos', N'U') AS [Value]").SingleAsync());
            // The visit stays as it was.
            var visit = await context.Appointments.SingleAsync(a => a.Id == appointmentId);
            Assert.Equal((AppointmentStatus.Requested, "Gotea el calentador"), (visit.Status, visit.Notes));
        }

        // Rolling back again does nothing, and forward again the visit takes photos.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = BeforeAppointmentRespondedAt.CreateContext(database, tenantId);
        var again = await forward.Appointments.SingleAsync(a => a.Id == appointmentId);
        forward.AppointmentProblemPhotos.Add(again.AddProblemPhoto(0, Key(tenantId), Now));
        await forward.SaveChangesAsync();
        Assert.Equal(1, await forward.AppointmentProblemPhotos.CountAsync());
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
            new string('v', 43));

    private static string Key(Guid tenantId) => $"orders/{tenantId}/visit-problem/202611/{Guid.NewGuid()}.webp";

    /// <summary>A trade with a visit requested online and the photos its customer sent.</summary>
    private static async Task<(Guid TenantId, Guid AppointmentId)> SeedAsync(SqlServerTestDatabase database, int photos)
    {
        var (tenant, settings, zone) = Business();
        var request = Request(tenant.Id, zone);
        await using var seed = BeforeAppointmentRespondedAt.CreateContext(database, tenant.Id);
        seed.AddRange(tenant, settings, zone, request);
        for (var n = 0; n < photos; n++)
            seed.AppointmentProblemPhotos.Add(request.AddProblemPhoto(n, Key(tenant.Id), Now));
        await seed.SaveChangesAsync();
        return (tenant.Id, request.Id);
    }

    private static Task InsertAsync(ApplicationDbContext context, Guid tenantId, Guid appointmentId, int position, string? key = null)
        => context.Database.ExecuteSqlRawAsync(
            "INSERT INTO [AppointmentProblemPhotos] ([Id], [TenantId], [AppointmentId], [Position], [FileKey], [AddedAt]) VALUES ({0}, {1}, {2}, {3}, {4}, {5})",
            Guid.NewGuid(), tenantId, appointmentId, position, key ?? Key(tenantId), Now);

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
