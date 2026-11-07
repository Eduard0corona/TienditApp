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
/// AddAppointmentAnswerReminderSentAt (Fase 29U, ADR 0075): when the business was reminded to answer a request online in
/// time. The requests that exist have not been reminded, the version before it keeps saving requests that have not been
/// either, and rolling back drops only the column, never a request, its status or its answer.
/// </summary>
public class AppointmentAnswerReminderMigrationSqlServerTests
{
    // The migration right before AddAppointmentAnswerReminderSentAt, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261106060148_AddTenantUserQuoteReplyEmails";
    private const string Migration = "20261107061906_AddAppointmentAnswerReminderSentAt";

    private static readonly DateTimeOffset Arrived = new(2027, 3, 1, 10, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task TheRequestsThatExist_ShouldNotHaveBeenReminded_AndAReminderFromNowOnIsKept()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var tenant = await SeedTenantAsync(database);
        await using (var before = BeforeAppointmentAnswerReminder.CreateContext(database, tenant.Id))
        {
            before.Appointments.Add(Request(tenant.Id));
            await before.SaveChangesAsync();
        }

        await MigrateAsync(database, Migration);

        await using var context = database.CreateContext(tenant.Id);
        Assert.Null((await context.Appointments.AsNoTracking().SingleAsync()).AnswerReminderSentAt);
        Assert.Equal("datetimeoffset|1|", await ColumnAsync(context));

        // Reminded an hour before its limit (it arrived without a time: a day after it arrived).
        var tracked = await context.Appointments.SingleAsync();
        Assert.True(tracked.RemindToAnswer(Arrived.AddHours(23)));
        await context.SaveChangesAsync();
        await using var check = database.CreateContext(tenant.Id);
        Assert.Equal(Arrived.AddHours(23), (await check.Appointments.AsNoTracking().SingleAsync()).AnswerReminderSentAt);
    }

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepSavingRequests_ThatHaveNotBeenReminded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);

        // The app as deployed before this version saves a request, unaware of the column.
        await using (var before = BeforeAppointmentAnswerReminder.CreateContext(database, tenant.Id))
        {
            before.Appointments.Add(Request(tenant.Id));
            await before.SaveChangesAsync();
        }

        await using var context = database.CreateContext(tenant.Id);
        Assert.Null((await context.Appointments.AsNoTracking().SingleAsync()).AnswerReminderSentAt);
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldDropOnlyTheColumn_AndForwardAgainNoRequestHasBeenReminded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);
        await using (var seed = database.CreateContext(tenant.Id))
        {
            var request = Request(tenant.Id);
            Assert.True(request.RemindToAnswer(Arrived.AddHours(23)));
            request.Accept(Arrived.AddHours(23).AddMinutes(10));
            seed.Appointments.Add(request);
            await seed.SaveChangesAsync();
        }

        await MigrateAsync(database, BeforeMigration);
        await using (var context = BeforeAppointmentAnswerReminder.CreateContext(database, tenant.Id))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Null(await context.Database.SqlQuery<int?>($"SELECT COL_LENGTH(N'dbo.Appointments', N'AnswerReminderSentAt') AS [Value]").SingleAsync());
            // The request stays as it was: accepted, with when it was answered.
            var request = await context.Appointments.AsNoTracking().SingleAsync();
            Assert.Equal((AppointmentStatus.Scheduled, (DateTimeOffset?)Arrived.AddHours(23).AddMinutes(10)), (request.Status, request.RespondedAt));
        }

        // Rolling back again does nothing; forward again, no request has been reminded.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(tenant.Id);
        Assert.Null((await forward.Appointments.AsNoTracking().SingleAsync()).AnswerReminderSentAt);
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
        await using var seed = BeforeAppointmentAnswerReminder.CreateContext(database);
        seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Appointments));
        await seed.SaveChangesAsync();
        return tenant;
    }

    /// <summary>The column's type, whether it takes NULL (1) and its default ('' for none).</summary>
    private static Task<string> ColumnAsync(ApplicationDbContext context)
        => context.Database.SqlQuery<string>($"""
            SELECT CONCAT(TYPE_NAME(c.[user_type_id]), N'|', c.[is_nullable], N'|', OBJECT_DEFINITION(c.[default_object_id])) AS [Value]
            FROM sys.columns c
            WHERE c.[object_id] = OBJECT_ID(N'dbo.Appointments') AND c.[name] = N'AnswerReminderSentAt'
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
