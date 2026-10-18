using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
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
/// AddOnlineBooking (Fase 24D, ADR 0025): the appointments and the settings that exist stay as the team's and with online
/// booking off; an appointment booked online keeps its account, its key and its link, one per business and request; the
/// version deployed before keeps booking and saving settings over the new columns; and rolling back stops with error 50019
/// once a customer booked online, and otherwise drops only the new columns. The database stops at this migration, so the
/// tests read it without what came after (<see cref="BeforeBusinessCover"/>).
/// </summary>
public class OnlineBookingMigrationSqlServerTests
{
    // The migration right before AddOnlineBooking, where its rollback.sql takes the database.
    private const string BeforeBooking = "20261017183622_AddServiceOrderPhotos";
    private const string Migration = "20261017225015_AddOnlineBooking";
    private static readonly DateTimeOffset Monday = new(2026, 10, 19, 16, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task ExistingRows_ShouldStayTheTeams_WithOnlineBookingOff()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeBooking);
        Guid tenantId;
        await using (var before = BeforeOnlineBooking.CreateContext(database))
        {
            var tenant = NewTenant();
            tenantId = tenant.Id;
            before.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Appointments));
            await before.SaveChangesAsync();
        }
        await using (var before = BeforeOnlineBooking.CreateContext(database, tenantId))
        {
            before.Appointments.Add(new Appointment(tenantId, "Valeria", "5522107765", "Corte", Monday, Monday.AddHours(1)));
            await before.SaveChangesAsync();
        }

        await MigrateAsync(database, Migration);

        await using var context = BeforeBusinessCover.CreateContext(database, tenantId);
        var settings = await context.TenantSettings.SingleAsync();
        Assert.Equal((false, 1), (settings.OnlineBookingEnabled, settings.OnlineBookingCapacity));
        var appointment = await context.Appointments.SingleAsync();
        Assert.Equal(
            (AppointmentChannel.Business, (Guid?)null, (Guid?)null, (string?)null, (DateTimeOffset?)null),
            (appointment.Channel, appointment.CustomerUserId, appointment.ClientRequestId, appointment.TrackingToken, appointment.CancelledByCustomerAt));
    }

    [SqlServerFact]
    public async Task OnlineAppointment_ShouldKeepItsLinkAndKey_OnePerRequest_AndAKnownChannelAndCapacity()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);
        var key = Guid.NewGuid();
        var token = new string('k', 43);
        await SeedOnlineAsync(database, tenant.Id, key, token);

        await using (var context = BeforeBusinessCover.CreateContext(database, tenant.Id))
        {
            var stored = await context.Appointments.SingleAsync();
            Assert.Equal((AppointmentStatus.Requested, AppointmentChannel.Online, (Guid?)key, token),
                (stored.Status, stored.Channel, stored.ClientRequestId, stored.TrackingToken));
        }

        // The same request again, or another one with the same link, collides.
        var sameKey = await Assert.ThrowsAsync<DbUpdateException>(() => SeedOnlineAsync(database, tenant.Id, key, new string('m', 43)));
        Assert.Contains("IX_Appointments_TenantId_ClientRequestId", sameKey.InnerException!.Message);
        var sameLink = await Assert.ThrowsAsync<DbUpdateException>(() => SeedOnlineAsync(database, tenant.Id, Guid.NewGuid(), token));
        Assert.Contains("IX_Appointments_TrackingToken", sameLink.InnerException!.Message);
        // The same key in another business is another request.
        var other = await SeedTenantAsync(database);
        await SeedOnlineAsync(database, other.Id, key, new string('n', 43));

        var channel = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database, $"UPDATE [Appointments] SET [Channel] = {2}"));
        Assert.Equal(547, channel.Number);
        Assert.Contains("CK_Appointments_Channel", channel.Message);
        var link = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database, $"UPDATE [Appointments] SET [TrackingToken] = {"corto"}"));
        Assert.Contains("CK_Appointments_TrackingToken", link.Message);
        var capacity = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database, $"UPDATE [TenantSettings] SET [OnlineBookingCapacity] = {21}"));
        Assert.Contains("CK_TenantSettings_OnlineBookingCapacity", capacity.Message);
    }

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepBookingAndSavingSettings_OverTheNewColumns()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database, bookingOn: true);

        // The version deployed before does not know the columns: it books and saves its settings as always.
        await using (var previous = BeforeOnlineBooking.CreateContext(database, tenant.Id))
        {
            previous.Appointments.Add(new Appointment(tenant.Id, "Valeria", "5522107765", "Corte", Monday, Monday.AddHours(1)));
            var settings = await previous.TenantSettings.SingleAsync();
            settings.SetHomeVisitTravelMinutes(45);
            await previous.SaveChangesAsync();
        }

        await using var check = BeforeBusinessCover.CreateContext(database, tenant.Id);
        Assert.Equal(AppointmentChannel.Business, (await check.Appointments.SingleAsync()).Channel);
        var saved = await check.TenantSettings.SingleAsync();
        Assert.Equal((true, 3, 45), (saved.OnlineBookingEnabled, saved.OnlineBookingCapacity, saved.HomeVisitTravelMinutes));
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceACustomerBookedOnline_AndThenDropOnlyTheColumns()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database, bookingOn: true);
        await SeedOnlineAsync(database, tenant.Id, Guid.NewGuid(), new string('k', 43));

        var error = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeBooking));
        Assert.Equal(50019, error.Number);
        Assert.Contains("Customers booked appointments online", error.Message);
        await using (var context = BeforeBusinessCover.CreateContext(database))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [Appointments] WHERE [Channel] = 1"));
            // The business declines it and books it itself instead: no appointment of a customer is left.
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE [Appointments] SET [Channel] = 0, [TrackingToken] = NULL, [ClientRequestId] = NULL, [Status] = 99");
        }

        await MigrateAsync(database, BeforeBooking);
        await using (var context = BeforeBusinessCover.CreateContext(database))
        {
            Assert.Equal(BeforeBooking, await LatestMigrationAsync(context));
            Assert.Equal((0, 0, 1), (
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE [object_id] = OBJECT_ID(N'dbo.Appointments') AND [name] IN (N'Channel', N'TrackingToken', N'CustomerUserId', N'ClientRequestId', N'CancelledByCustomerAt')"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE [object_id] = OBJECT_ID(N'dbo.TenantSettings') AND [name] LIKE N'OnlineBooking%'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [Appointments]")));
        }

        // Rolling back again does nothing, and forward again the columns are back with their defaults.
        await MigrateAsync(database, BeforeBooking);
        await MigrateAsync(database, Migration);
        await using var forward = BeforeBusinessCover.CreateContext(database, tenant.Id);
        var settings = await forward.TenantSettings.SingleAsync();
        Assert.Equal((false, 1), (settings.OnlineBookingEnabled, settings.OnlineBookingCapacity));
        Assert.Equal(AppointmentChannel.Business, (await forward.Appointments.SingleAsync()).Channel);
    }

    private static async Task SeedOnlineAsync(SqlServerTestDatabase database, Guid tenantId, Guid key, string token)
    {
        await using var context = BeforeBusinessCover.CreateContext(database, tenantId);
        context.Appointments.Add(Appointment.RequestOnline(
            tenantId, "Valeria", "+525522107765", Guid.NewGuid(), "Corte de cabello", Monday, Monday.AddHours(1),
            null, null, key, token));
        await context.SaveChangesAsync();
    }

    private static Tenant NewTenant()
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        return new Tenant($"Barbería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
    }

    private static async Task<Tenant> SeedTenantAsync(SqlServerTestDatabase database, bool bookingOn = false)
    {
        var tenant = NewTenant();
        var settings = new TenantSettings(tenant.Id, TenantCapability.Appointments);
        if (bookingOn)
            settings.UpdateOnlineBooking(true, 3);
        await using var seed = BeforeBusinessCover.CreateContext(database);
        seed.AddRange(tenant, settings);
        await seed.SaveChangesAsync();
        return tenant;
    }

    /// <summary>Applies the migrations up to <paramref name="target"/>, as a deployment does.</summary>
    private static async Task MigrateAsync(SqlServerTestDatabase database, string target)
    {
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task ExecuteAsync(SqlServerTestDatabase database, FormattableString sql)
    {
        await using var context = database.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    private static Task<string> LatestMigrationAsync(ApplicationDbContext context) =>
        context.Database.SqlQuery<string>($"""
            SELECT TOP (1) [MigrationId] AS [Value] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC
            """).SingleAsync();

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql) =>
        context.Database.SqlQuery<int>(sql).SingleAsync();
}
