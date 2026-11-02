using FoodParkDigital.Domain.Appointments;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
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
/// AddAppointmentWorkOrder (Fase 29F, ADR 0053 point 17): the appointments that do the job of a service order. The version
/// before it keeps saving its appointments over the new column; the database refuses a job appointment the aggregate never
/// writes (with a sale of its own, booked online, with a link, or of an order that does not exist); rolling back stops with
/// error 50054 once an appointment does the job of an order, and otherwise drops only what it added.
/// </summary>
public class AppointmentWorkMigrationSqlServerTests
{
    // The migration right before AddAppointmentWorkOrder, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261102014702_AddQuoteConversion";
    private const string Migration = "20261102073115_AddAppointmentWorkOrder";
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 16, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepSavingItsAppointments_OverTheNewColumn()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);

        // The app as deployed before this version agenda an appointment and charges it, unaware of the new column.
        Guid tenantId;
        Guid appointmentId;
        await using (var before = BeforeAppointmentWork.CreateContext(database))
        {
            var (tenant, settings) = Business();
            var appointment = new Appointment(tenant.Id, "Valeria", "5522107765", "Revisión", Start, Start.AddHours(1));
            var sale = Order(tenant.Id, "ORD-000001");
            appointment.RecordSale(sale.Id);
            before.AddRange(tenant, settings, sale, appointment);
            await before.SaveChangesAsync();
            (tenantId, appointmentId) = (tenant.Id, appointment.Id);
        }

        await using var context = database.CreateContext(tenantId);
        var saved = await context.Appointments.SingleAsync(a => a.Id == appointmentId);
        Assert.Equal(((Guid?)null, false), (saved.WorkOrderId, saved.IsWork));
        Assert.NotNull(saved.SaleOrderId);
    }

    [SqlServerFact]
    public async Task TheDatabase_ShouldRefuse_AJobAppointmentTheAggregateNeverWrites()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var (tenantId, appointmentId, orderId) = await SeedAsync(database);
        Guid saleId;
        await using (var context = database.CreateContext(tenantId))
        {
            var sale = Order(tenantId, "ORD-000003");
            context.Orders.Add(sale);
            await context.SaveChangesAsync();
            saleId = sale.Id;
        }

        await using var check = database.CreateContext(tenantId);
        foreach (var sql in new[]
        {
            // A sale of its own: the job is charged in its order.
            $"UPDATE [Appointments] SET [SaleOrderId] = '{saleId}' WHERE [Id] = '{appointmentId}'",
            // Booked online, or with a link to follow it: the job is agendado by the team.
            $"UPDATE [Appointments] SET [Channel] = 1 WHERE [Id] = '{appointmentId}'",
            $"UPDATE [Appointments] SET [TrackingToken] = '{new string('t', Domain.Orders.Entities.Order.TrackingTokenLength)}' WHERE [Id] = '{appointmentId}'",
        })
        {
            var refused = await Record.ExceptionAsync(() => check.Database.ExecuteSqlRawAsync(sql));
            Assert.Contains("CK_Appointments_Work", refused?.Message);
        }

        // The job of an order that does not exist.
        var nowhere = await Record.ExceptionAsync(() => check.Database.ExecuteSqlRawAsync(
            $"UPDATE [Appointments] SET [WorkOrderId] = '{Guid.NewGuid()}' WHERE [Id] = '{appointmentId}'"));
        Assert.Contains("FK_Appointments_Orders_WorkOrderId", nowhere?.Message);

        // What the aggregate writes stays: the job of its order, without a sale.
        var saved = await check.Appointments.AsNoTracking().SingleAsync(a => a.Id == appointmentId);
        Assert.Equal((orderId, (Guid?)null), (saved.WorkOrderId, saved.SaleOrderId));
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceAnAppointmentDoesAJob_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var (tenantId, appointmentId, orderId) = await SeedAsync(database);

        var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeMigration));
        Assert.True(stopped is SqlException { Number: 50054 }, stopped?.Message ?? "it was not stopped");
        await using (var context = database.CreateContext(tenantId))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Equal(orderId, (await context.Appointments.SingleAsync(a => a.Id == appointmentId)).WorkOrderId);
            // Removed on purpose, so it rolls back.
            await context.Database.ExecuteSqlRawAsync("UPDATE [Appointments] SET [WorkOrderId] = NULL");
        }

        await MigrateAsync(database, BeforeMigration);
        await using (var context = BeforeAppointmentWork.CreateContext(database))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Equal(0, await context.Database.SqlQuery<int>($"""
                SELECT (SELECT COUNT(*) FROM sys.columns WHERE [object_id] = OBJECT_ID(N'dbo.Appointments') AND [name] = N'WorkOrderId')
                    + (SELECT COUNT(*) FROM sys.check_constraints WHERE [name] = N'CK_Appointments_Work')
                    + (SELECT COUNT(*) FROM sys.indexes WHERE [name] = N'IX_Appointments_WorkOrderId')
                    + (SELECT COUNT(*) FROM sys.foreign_keys WHERE [name] = N'FK_Appointments_Orders_WorkOrderId') AS [Value]
                """).SingleAsync());
            // The appointment and the order stay.
            Assert.Equal((1, 1), (await context.Appointments.IgnoreQueryFilters().CountAsync(), await context.Orders.IgnoreQueryFilters().CountAsync()));
        }

        // Rolling back again does nothing, and forward again the appointment does no job.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(tenantId);
        Assert.Null((await forward.Appointments.SingleAsync()).WorkOrderId);
    }

    private static (Tenant Tenant, TenantSettings Settings) Business()
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Plomería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
        return (tenant, new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments));
    }

    /// <summary>A trade with the service order of an accepted quote and the appointment that does its job.</summary>
    private static async Task<(Guid TenantId, Guid AppointmentId, Guid OrderId)> SeedAsync(SqlServerTestDatabase database)
    {
        var (tenant, settings) = Business();
        var order = Order(tenant.Id, "ORD-000002");
        var appointment = Appointment.ForWork(
            tenant.Id, order.Id, "Valeria", "5522107765", AppointmentWorkRules.ServiceName("COT-000001"), Start, Start.AddHours(3), null);
        await using var seed = database.CreateContext(tenant.Id);
        seed.AddRange(tenant, settings, order, appointment);
        await seed.SaveChangesAsync();
        return (tenant.Id, appointment.Id, order.Id);
    }

    private static Order Order(Guid tenantId, string number)
    {
        var order = new Order(tenantId, null, null, true, number, BusinessType.Service, OrderType.Service, "Valeria",
            "+525522107765", null, null, pricesIncludeTax: true);
        order.AddItem(Guid.NewGuid(), "Cambio de calentador", 1, 1160m);
        return order;
    }

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
