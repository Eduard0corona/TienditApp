using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Tenants;
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
/// AddAppointmentDeposits (Fase 31O, ADR 0095): the share of a deposit a business asks to book online, the deposit an
/// appointment was booked with and where it stands, and the checkout that books an appointment. What exists asks none and
/// books none; the database refuses what the aggregates never write (a share outside 10 to 100, a deposit of nothing, in an
/// unknown state or on an appointment of the team, a checkout of a quote that books an appointment, one paid that booked
/// nothing, two that booked the same appointment, or a refund of a deposit without its checkout); the version before it keeps
/// saving appointments and checkouts; rolling back stops with error 50063 while a business asks a deposit, an appointment
/// has one or a checkout went to book one, and otherwise drops only what it added.
/// </summary>
public class AppointmentDepositsMigrationSqlServerTests
{
    // The migration right before AddAppointmentDeposits, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261113033616_AddQuoteDeposits";
    private const string Migration = "20261114111542_AddAppointmentDeposits";
    private const long ProviderUserId = 1234567890;
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 14);

    [SqlServerFact]
    public async Task WhatExists_ShouldAskNoDeposit_AndTheDatabaseShouldRefuseWhatTheAggregatesNeverWrite()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var seed = await SeedAsync(BeforeAppointmentDeposits.CreateContext(database));

        await MigrateAsync(database, Migration);

        await using (var context = database.CreateContext(seed.TenantId))
        {
            Assert.Null((await context.TenantSettings.AsNoTracking().SingleAsync()).BookingDepositPercent);
            Assert.All(await context.Appointments.AsNoTracking().ToListAsync(),
                a => Assert.Equal(((decimal?)null, (AppointmentDepositStatus?)null), (a.DepositAmount, a.DepositStatus)));
            Assert.All(await context.OnlineCheckouts.AsNoTracking().ToListAsync(),
                c => Assert.Equal((false, (Guid?)null), (c.ForAppointment, c.AppointmentId)));
        }

        // What the domain writes is kept: the share, a request booked with 105, the checkout that booked it and its refund.
        Guid booked;
        Guid paid;
        await using (var write = database.CreateContext(seed.TenantId))
        {
            (await write.TenantSettings.SingleAsync()).SetBookingDepositPercent(30);
            var appointment = Request(seed.TenantId, deposit: 105m);
            var checkout = OnlineCheckout.ForAppointmentRequest(seed.TenantId, ProviderUserId, 105m, "{\"serviceProductId\":1}", null, "+525522107765", Now);
            write.Appointments.Add(appointment);
            write.OnlineCheckouts.Add(checkout);
            await write.SaveChangesAsync();
            checkout.PlaceAppointment("98765", Now.AddMinutes(5), appointment.Id);
            appointment.Decline(Now.AddMinutes(10));
            write.OnlinePaymentRefunds.Add(OnlinePaymentRefund.ForAppointmentDeposit(seed.TenantId, checkout.Id, "98765", 105m, Now.AddMinutes(10)));
            await write.SaveChangesAsync();
            (booked, paid) = (appointment.Id, checkout.Id);
        }
        await using var check = database.CreateContext(seed.TenantId);
        Assert.Equal(30, (await check.TenantSettings.AsNoTracking().SingleAsync()).BookingDepositPercent);
        var stored = await check.Appointments.AsNoTracking().SingleAsync(a => a.Id == booked);
        Assert.Equal(((decimal?)105m, (AppointmentDepositStatus?)AppointmentDepositStatus.Returned), (stored.DepositAmount, stored.DepositStatus));
        Assert.Equal((true, (Guid?)booked), await check.OnlineCheckouts.AsNoTracking().Where(c => c.Id == paid)
            .Select(c => new ValueTuple<bool, Guid?>(c.ForAppointment, c.AppointmentId)).SingleAsync());

        foreach (var (sql, constraint) in new (string, string)[]
        {
            ($"UPDATE [TenantSettings] SET [BookingDepositPercent] = 5 WHERE [TenantId] = '{seed.TenantId}'", "CK_TenantSettings_BookingDepositPercent"),
            ($"UPDATE [TenantSettings] SET [BookingDepositPercent] = 101 WHERE [TenantId] = '{seed.TenantId}'", "CK_TenantSettings_BookingDepositPercent"),
            ($"UPDATE [Appointments] SET [DepositAmount] = 0 WHERE [Id] = '{booked}'", "CK_Appointments_Deposit"),
            ($"UPDATE [Appointments] SET [DepositStatus] = 5 WHERE [Id] = '{booked}'", "CK_Appointments_Deposit"),
            ($"UPDATE [Appointments] SET [DepositStatus] = NULL WHERE [Id] = '{booked}'", "CK_Appointments_Deposit"),
            ($"UPDATE [Appointments] SET [DepositAmount] = NULL WHERE [Id] = '{booked}'", "CK_Appointments_Deposit"),
            // One the team booked never has one.
            ($"UPDATE [Appointments] SET [DepositAmount] = 105, [DepositStatus] = 1 WHERE [Id] = '{seed.TeamAppointmentId}'", "CK_Appointments_Deposit"),
            // A deposit of a quote never books an appointment.
            ($"UPDATE [OnlineCheckouts] SET [ForAppointment] = 1 WHERE [QuoteId] IS NOT NULL", "CK_OnlineCheckouts_Appointment"),
            // Paid, it keeps nothing of what was asked.
            ($"UPDATE [OnlineCheckouts] SET [Request] = N'[]' WHERE [Id] = '{paid}'", "CK_OnlineCheckouts_Outcome"),
            // An appointment that does not exist, and a second checkout of the same one.
            ($"UPDATE [OnlineCheckouts] SET [AppointmentId] = '{Guid.NewGuid()}' WHERE [Id] = '{paid}'", "FK_OnlineCheckouts_Appointments_AppointmentId"),
            ($"""
              INSERT INTO [OnlineCheckouts] ([Id], [TenantId], [ProviderUserId], [Amount], [Status], [StartedAt], [ExpiresAt], [PaymentId], [PaidAt],
                  [ForAppointment], [AppointmentId], [Dispute], [CreatedAt])
              VALUES (NEWID(), '{seed.TenantId}', {ProviderUserId}, 105, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'98766', SYSDATETIMEOFFSET(),
                  1, '{booked}', 0, SYSDATETIMEOFFSET())
              """, "IX_OnlineCheckouts_AppointmentId"),
            // The refund of a deposit comes from its checkout.
            ("UPDATE [OnlinePaymentRefunds] SET [CheckoutId] = NULL WHERE [Reason] = 7", "CK_OnlinePaymentRefunds_Link"),
        })
        {
            var error = await Record.ExceptionAsync(() => check.Database.ExecuteSqlRawAsync(sql));
            Assert.True(error is SqlException, $"Not refused ({error?.GetType().Name}): {sql}");
            Assert.Contains(constraint, error!.Message);
        }
    }

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepSavingAppointmentsAndCheckouts_OverTheNewColumns()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);

        // The app as deployed before this version, unaware of the columns.
        var seed = await SeedAsync(BeforeAppointmentDeposits.CreateContext(database));

        await using var context = database.CreateContext(seed.TenantId);
        var request = await context.Appointments.AsNoTracking().SingleAsync(a => a.Id == seed.RequestId);
        Assert.Equal((AppointmentStatus.Requested, (decimal?)null, (AppointmentDepositStatus?)null), (request.Status, request.DepositAmount, request.DepositStatus));
        Assert.Equal(2, await context.OnlineCheckouts.CountAsync(c => !c.ForAppointment && c.AppointmentId == null));
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_WhileADepositIsAskedOrPaid_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database.CreateContext());

        // Each of the three alone stops it: the business's share, an appointment booked with one, and a checkout to book one.
        foreach (var leaveOnlyOne in new[]
        {
            $"UPDATE [TenantSettings] SET [BookingDepositPercent] = 30 WHERE [TenantId] = '{seed.TenantId}'",
            $"UPDATE [TenantSettings] SET [BookingDepositPercent] = NULL; UPDATE [Appointments] SET [DepositAmount] = 105, [DepositStatus] = 4 WHERE [Id] = '{seed.RequestId}'",
            $"UPDATE [Appointments] SET [DepositAmount] = NULL, [DepositStatus] = NULL WHERE [Id] = '{seed.RequestId}'"
        })
        {
            await using (var context = database.CreateContext(seed.TenantId))
            {
                await context.Database.ExecuteSqlRawAsync(leaveOnlyOne);
                if (!await context.TenantSettings.AnyAsync(t => t.BookingDepositPercent != null)
                    && !await context.Appointments.AnyAsync(a => a.DepositAmount != null))
                {
                    context.OnlineCheckouts.Add(OnlineCheckout.ForAppointmentRequest(seed.TenantId, ProviderUserId, 105m, "{}", null, "+525522107765", Now));
                    await context.SaveChangesAsync();
                }
            }
            var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeMigration));
            Assert.True(stopped is SqlException { Number: 50063 }, stopped?.Message ?? "it was not stopped");
            await using var after = database.CreateContext(seed.TenantId);
            Assert.Equal(Migration, await LatestMigrationAsync(after));
        }
        await using (var clear = database.CreateContext(seed.TenantId))
            await clear.Database.ExecuteSqlRawAsync("DELETE FROM [OnlineCheckouts] WHERE [ForAppointment] = 1");

        // Without any, it drops only what it added: the appointments and the checkouts of the menu and the quote stay.
        await MigrateAsync(database, BeforeMigration);
        await using (var context = BeforeAppointmentDeposits.CreateContext(database, seed.TenantId))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            foreach (var (table, column) in new[]
            {
                ("TenantSettings", "BookingDepositPercent"), ("Appointments", "DepositAmount"), ("Appointments", "DepositStatus"),
                ("OnlineCheckouts", "ForAppointment"), ("OnlineCheckouts", "AppointmentId")
            })
                Assert.Null(await context.Database.SqlQueryRaw<int?>($"SELECT COL_LENGTH(N'dbo.{table}', N'{column}') AS [Value]").SingleAsync());
            Assert.Equal(0, await context.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS [Value] FROM sys.check_constraints
                WHERE [name] IN (N'CK_TenantSettings_BookingDepositPercent', N'CK_Appointments_Deposit', N'CK_OnlineCheckouts_Appointment')
                """).SingleAsync());
            // The outcome and link checks are back as they were.
            Assert.DoesNotContain("AppointmentId", await context.Database.SqlQuery<string>($"""
                SELECT [definition] AS [Value] FROM sys.check_constraints WHERE [name] = N'CK_OnlineCheckouts_Outcome'
                """).SingleAsync());
            Assert.DoesNotContain("(7)", await context.Database.SqlQuery<string>($"""
                SELECT [definition] AS [Value] FROM sys.check_constraints WHERE [name] = N'CK_OnlinePaymentRefunds_Link'
                """).SingleAsync());
            Assert.Equal(2, await context.Appointments.AsNoTracking().CountAsync());
            Assert.Equal(2, await context.OnlineCheckouts.AsNoTracking().CountAsync());
        }

        // Rolling back again does nothing; forward again, nothing asks a deposit.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(seed.TenantId);
        Assert.All(await forward.Appointments.AsNoTracking().ToListAsync(), appointment => Assert.Null(appointment.DepositStatus));
    }

    private sealed record Seed(Guid TenantId, Guid RequestId, Guid TeamAppointmentId);

    /// <summary>
    /// A barbershop with a request online, an appointment of its team, the checkout of an order of its menu and the deposit of a
    /// quote, saved by <paramref name="seed"/>.
    /// </summary>
    private static async Task<Seed> SeedAsync(ApplicationDbContext seed)
    {
        await using (seed)
        {
            var slug = Guid.NewGuid().ToString("N")[..10];
            var tenant = new Tenant($"Barbería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
            var request = Request(tenant.Id, deposit: null);
            var team = new Appointment(tenant.Id, "Luis", "+525522107766", "Barba", Now.AddDays(2), Now.AddDays(2).AddHours(1));
            var quote = Quote.Start(tenant.Id, Folios.QuoteNumber(1), new QuoteCustomer(null, "Valeria", "5522107765", null), null, true, 15, Now);
            quote.ReplaceLines([new QuoteLineInput(null, "Cambio de calentador", 1m, 1160m, VatRate.General)], Now);
            quote.Send(Today, new string('b', 43), null, Now, 348m);
            seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments), request, team, quote,
                OnlineCheckout.Start(tenant.Id, ProviderUserId, 63.80m, "{\"items\":[]}", null, "+525522107765", Now),
                OnlineCheckout.ForQuote(tenant.Id, ProviderUserId, quote.Id, 348m, Now));
            await seed.SaveChangesAsync();
            return new Seed(tenant.Id, request.Id, team.Id);
        }
    }

    private static Appointment Request(Guid tenantId, decimal? deposit)
        => Appointment.RequestOnline(
            tenantId, "Valeria", "+525522107765", Guid.NewGuid(), "Corte de cabello", Now.AddDays(3), Now.AddDays(3).AddHours(1),
            null, null, Guid.NewGuid(), new string(deposit is null ? 'a' : 'c', 43), deposit: deposit);

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
