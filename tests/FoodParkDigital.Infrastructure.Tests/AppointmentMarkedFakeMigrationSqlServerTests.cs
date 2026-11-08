using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// AddAppointmentMarkedFakeAt (Fase 29V, ADR 0076): when the business declined a request online as fake, and when it took the
/// mark back. The requests that exist are not marked, the version before it keeps saving requests that are not either, and
/// rolling back drops only the columns: a request marked stays cancelled, without an answer.
/// </summary>
public class AppointmentMarkedFakeMigrationSqlServerTests
{
    // The migration right before AddAppointmentMarkedFakeAt, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261107061906_AddAppointmentAnswerReminderSentAt";
    private const string Migration = "20261107190735_AddAppointmentMarkedFakeAt";

    private static readonly DateTimeOffset Arrived = new(2027, 3, 1, 10, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task TheRequestsThatExist_ShouldNotBeMarked_AndAMarkFromNowOnIsKept_AndTakenBack()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var tenant = await SeedTenantAsync(database);
        await using (var before = BeforeAppointmentMarkedFake.CreateContext(database, tenant.Id))
        {
            before.Appointments.Add(Request(tenant.Id));
            await before.SaveChangesAsync();
        }

        await MigrateAsync(database, Migration);

        await using var context = BeforeOrderCancellationEmails.CreateContext(database, tenant.Id);
        Assert.Null((await context.Appointments.AsNoTracking().SingleAsync()).MarkedFakeAt);
        Assert.Equal("datetimeoffset|1|", await ColumnAsync(context, "MarkedFakeAt"));
        Assert.Equal("datetimeoffset|1|", await ColumnAsync(context, "UnmarkedFakeAt"));

        var marked = Arrived.AddHours(2);
        var tracked = await context.Appointments.SingleAsync();
        tracked.MarkFake(marked);
        await context.SaveChangesAsync();
        await using (var check = BeforeOrderCancellationEmails.CreateContext(database, tenant.Id))
        {
            var stored = await check.Appointments.AsNoTracking().SingleAsync();
            Assert.Equal(
                (AppointmentStatus.Cancelled, (DateTimeOffset?)marked, (DateTimeOffset?)null),
                (stored.Status, stored.MarkedFakeAt, stored.RespondedAt));
        }

        // Taken back: declined then, and when it was marked stays kept.
        var takenBack = Arrived.AddDays(1);
        tracked.UnmarkFake(takenBack);
        await context.SaveChangesAsync();
        await using var after = BeforeOrderCancellationEmails.CreateContext(database, tenant.Id);
        var unmarked = await after.Appointments.AsNoTracking().SingleAsync();
        Assert.Equal(
            ((DateTimeOffset?)marked, (DateTimeOffset?)takenBack, (DateTimeOffset?)takenBack),
            (unmarked.MarkedFakeAt, unmarked.UnmarkedFakeAt, unmarked.RespondedAt));
    }

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepSavingRequests_ThatAreNotMarked()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);

        // The app as deployed before this version saves a request, unaware of the column.
        await using (var before = BeforeAppointmentMarkedFake.CreateContext(database, tenant.Id))
        {
            before.Appointments.Add(Request(tenant.Id));
            await before.SaveChangesAsync();
        }

        await using var context = BeforeOrderCancellationEmails.CreateContext(database, tenant.Id);
        Assert.Null((await context.Appointments.AsNoTracking().SingleAsync()).MarkedFakeAt);
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldDropOnlyTheColumn_AndForwardAgainNoRequestIsMarked()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);
        await using (var seed = BeforeOrderCancellationEmails.CreateContext(database, tenant.Id))
        {
            var request = Request(tenant.Id);
            request.MarkFake(Arrived.AddHours(2));
            seed.Appointments.Add(request);
            await seed.SaveChangesAsync();
        }

        await MigrateAsync(database, BeforeMigration);
        await using (var context = BeforeAppointmentMarkedFake.CreateContext(database, tenant.Id))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Null(await context.Database.SqlQuery<int?>($"SELECT COL_LENGTH(N'dbo.Appointments', N'MarkedFakeAt') AS [Value]").SingleAsync());
            Assert.Null(await context.Database.SqlQuery<int?>($"SELECT COL_LENGTH(N'dbo.Appointments', N'UnmarkedFakeAt') AS [Value]").SingleAsync());
            // The request stays as it was: cancelled, without an answer, so it does not count in Explorar either.
            var request = await context.Appointments.AsNoTracking().SingleAsync();
            Assert.Equal((AppointmentStatus.Cancelled, (DateTimeOffset?)null), (request.Status, request.RespondedAt));
        }

        // Rolling back again does nothing; forward again, no request is marked.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = BeforeOrderCancellationEmails.CreateContext(database, tenant.Id);
        Assert.Null((await forward.Appointments.AsNoTracking().SingleAsync()).MarkedFakeAt);
    }

    private static Appointment Request(Guid tenantId)
    {
        var start = Arrived.AddDays(2);
        var request = Appointment.RequestOnline(
            tenantId, "Valeria", "+525522107765", Guid.NewGuid(), "Corte de cabello", start, start.AddHours(1),
            null, null, Guid.NewGuid(), (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))[..43]);
        request.CreatedAt = Arrived;
        return request;
    }

    private static async Task<Tenant> SeedTenantAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Barbería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
        await using var seed = BeforeAppointmentMarkedFake.CreateContext(database);
        seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Appointments));
        await seed.SaveChangesAsync();
        return tenant;
    }

    /// <summary>The column's type, whether it takes NULL (1) and its default ('' for none).</summary>
    private static Task<string> ColumnAsync(ApplicationDbContext context, string column)
        => context.Database.SqlQuery<string>($"""
            SELECT CONCAT(TYPE_NAME(c.[user_type_id]), N'|', c.[is_nullable], N'|', OBJECT_DEFINITION(c.[default_object_id])) AS [Value]
            FROM sys.columns c
            WHERE c.[object_id] = OBJECT_ID(N'dbo.Appointments') AND c.[name] = {column}
            """).SingleAsync();

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
