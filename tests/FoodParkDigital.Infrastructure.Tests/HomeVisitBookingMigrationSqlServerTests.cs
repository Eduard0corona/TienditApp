using FoodParkDigital.Domain.Appointments.Entities;
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
/// AddHomeVisitBooking (Fase 29B, ADR 0053): a visit keeps how a visit to quote requested online is charged and an
/// appointment the customer's email for its quote. The version before it keeps saving appointments and visits over the new
/// columns, which stay empty; rolling back stops with error 50050 once a customer requested a visit online, and otherwise
/// drops only the two columns, and a second run does nothing.
/// </summary>
public class HomeVisitBookingMigrationSqlServerTests
{
    // The migration right before AddHomeVisitBooking, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261031085333_AddHomeVisitRequests";
    private const string Migration = "20261101002207_AddHomeVisitBooking";
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 16, 0, 0, TimeSpan.Zero);
    private static readonly DeliveryAddress Address = DeliveryAddress.ForVisit("72000", "Av. Juárez 123, Col. Centro", null);

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepSavingAppointmentsAndVisits_LeavingTheNewColumnsEmpty()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var (tenantId, zoneId) = await SeedAsync(database);

        // The app as deployed before this version arranges a visit in the agenda, unaware of the new columns.
        await using (var before = BeforeHomeVisitBooking.CreateContext(database, tenantId))
        {
            var appointment = new Appointment(tenantId, "Ana", "5512345678", "Revisión", Start, Start.AddHours(1));
            appointment.ArrangeVisit(Address, await ZoneAsync(before, zoneId), Start);
            before.Appointments.Add(appointment);
            await before.SaveChangesAsync();
        }

        await using var context = database.CreateContext(tenantId);
        var stored = await context.Appointments.SingleAsync();
        Assert.Equal(((string?)null, (HomeVisitChargePolicy?)null, 50m), (stored.CustomerEmail, stored.Visit!.ChargePolicy, stored.Visit.Fee));
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceACustomerRequestedAVisitOnline_AndThenDropOnlyTheColumns()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var (tenantId, zoneId) = await SeedAsync(database);
        await using (var context = database.CreateContext(tenantId))
        {
            context.Appointments.Add(Appointment.RequestHomeVisit(
                tenantId, "Valeria", "+525522107765", null, HomeVisitRules.DefaultVisitName, Start, Start.AddHours(1),
                "Gotea el calentador", "valeria@correo.mx", Address, await ZoneAsync(context, zoneId), HomeVisitChargePolicy.Free,
                null, Guid.NewGuid(), new string('a', 43)));
            await context.SaveChangesAsync();
        }

        // How the visit is charged stops it; so does the customer's email alone, each emptied on purpose.
        foreach (var cleanUp in new[] { "UPDATE [AppointmentVisits] SET [ChargePolicy] = NULL", "UPDATE [Appointments] SET [CustomerEmail] = NULL" })
        {
            var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeMigration));
            Assert.True(stopped is SqlException { Number: 50050 }, stopped?.Message ?? "it was not stopped");
            await using var context = database.CreateContext(tenantId);
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            await context.Database.ExecuteSqlRawAsync(cleanUp);
        }

        await MigrateAsync(database, BeforeMigration);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Equal(0, await context.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS [Value] FROM sys.columns
                WHERE ([object_id] = OBJECT_ID(N'dbo.AppointmentVisits') AND [name] = N'ChargePolicy')
                    OR ([object_id] = OBJECT_ID(N'dbo.Appointments') AND [name] = N'CustomerEmail')
                """).SingleAsync());
            Assert.Equal(0, await context.Database.SqlQuery<int>(
                $"SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE [name] = N'CK_AppointmentVisits_ChargePolicy'").SingleAsync());
            // The appointment and its visit stay.
            Assert.Equal((1, 1), (
                await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [Appointments]").SingleAsync(),
                await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [AppointmentVisits]").SingleAsync()));
        }

        // Rolling back again does nothing, and forward again the columns are back, empty.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(tenantId);
        var appointment = await forward.Appointments.SingleAsync();
        Assert.Equal(((string?)null, (HomeVisitChargePolicy?)null, 0m), (appointment.CustomerEmail, appointment.Visit!.ChargePolicy, appointment.Visit.Fee));
    }

    /// <summary>A trade with a zone that goes to 72000 for $50.</summary>
    private static async Task<(Guid TenantId, Guid ZoneId)> SeedAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Plomería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
        var settings = new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments);
        var zone = new DeliveryZone(tenant.Id, new DeliveryZoneFields("Centro", 50m, 0m, null, ["72000"]));
        await using var seed = database.CreateContext(tenant.Id);
        seed.AddRange(tenant, settings, zone);
        await seed.SaveChangesAsync();
        return (tenant.Id, zone.Id);
    }

    private static Task<DeliveryZone> ZoneAsync(ApplicationDbContext context, Guid zoneId)
        => context.DeliveryZones.Include(zone => zone.PostalCodes).SingleAsync(zone => zone.Id == zoneId);

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
