using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
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
/// Appointments at the customer's address (Fase 22E): the visit saved and loaded with its appointment and kept in its
/// business, the visit charge with its sale, an appointment charged once and caught by its version, the checks keeping
/// every row one the domain could write, the version deployed before still taking appointments and settings, and rolling
/// back stopped while there is any visit or charge and otherwise dropping only what it added.
/// </summary>
public class HomeVisitsSqlServerTests
{
    private const string MigrationBefore = "20261013085926_AddCourierSettlements";
    private const string Migration = "20261013191311_AddHomeVisits";
    private static readonly DateTimeOffset Start = new(2026, 10, 14, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 17, 0, 0, TimeSpan.Zero);
    private static readonly DeliveryAddress Address = DeliveryAddress.ForVisit("72000", "Av. Juárez 123, Col. Centro", "Portón negro");

    [SqlServerFact]
    public async Task Visit_ShouldBeSavedAndLoaded_WithItsAppointment_InItsBusiness()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, other) = await SeedAsync(database);
        var appointmentId = await SeedAppointmentAsync(database, tenant.Id, visit: true);

        await using (var check = database.CreateContext(tenant.Id))
        {
            // No Include: the agenda, the travel time and the sale always need it.
            var visit = (await check.Appointments.SingleAsync(a => a.Id == appointmentId)).Visit!;
            Assert.Equal((tenant.Id, "72000", "Av. Juárez 123, Col. Centro", "Portón negro", "Centro", 80m),
                (visit.TenantId, visit.PostalCode, visit.AddressLine, visit.References, visit.ZoneName, visit.Fee));
        }

        await using var neighbor = database.CreateContext(other.Id);
        Assert.Empty(await neighbor.AppointmentVisits.ToListAsync());
    }

    [SqlServerFact]
    public async Task Visit_ShouldMoveInItsRow_AndGoAway_WhenTheCustomerComesToTheBusiness()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var appointmentId = await SeedAppointmentAsync(database, tenant.Id, visit: true);
        Guid visitId;
        await using (var context = database.CreateContext(tenant.Id))
        {
            var appointment = await context.Appointments.SingleAsync(a => a.Id == appointmentId);
            visitId = appointment.Visit!.Id;
            appointment.ArrangeVisit(DeliveryAddress.ForVisit("72005", "Calle 5 Sur 300", null), Zone(tenant.Id, fee: 0m), Now);
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext(tenant.Id))
        {
            var visit = await context.AppointmentVisits.SingleAsync();
            Assert.Equal((visitId, "72005", (string?)null, 0m), (visit.Id, visit.PostalCode, visit.References, visit.Fee));

            (await context.Appointments.SingleAsync(a => a.Id == appointmentId)).MoveToBusiness(Now);
            await context.SaveChangesAsync();
        }

        await using var check = database.CreateContext(tenant.Id);
        Assert.Empty(await check.AppointmentVisits.ToListAsync());
        Assert.Null((await check.Appointments.SingleAsync()).Visit);
    }

    [SqlServerFact]
    public async Task Sale_ShouldBeSavedWithItsVisitCharge_AndTheAppointmentDone()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, other) = await SeedAsync(database);
        var appointmentId = await SeedAppointmentAsync(database, tenant.Id, visit: true);
        var orderId = await ChargeAsync(database, tenant.Id, appointmentId);

        await using (var check = database.CreateContext(tenant.Id))
        {
            // No Include either: the charge is part of the total.
            var order = await check.Orders.SingleAsync(o => o.Id == orderId);
            var charge = order.VisitCharge!;
            Assert.Equal((appointmentId, "Centro", 80m, 16m), (charge.AppointmentId, charge.ZoneName, charge.Fee, charge.FeeTaxRate));
            Assert.Equal(480m, order.Total);
            var appointment = await check.Appointments.SingleAsync(a => a.Id == appointmentId);
            Assert.Equal((orderId, AppointmentStatus.Completed), (appointment.SaleOrderId, appointment.Status));
        }

        await using var neighbor = database.CreateContext(other.Id);
        Assert.Empty(await neighbor.OrderVisitCharges.ToListAsync());
    }

    [SqlServerFact]
    public async Task Appointment_ShouldBeChargedOnce_ByItsVersion_WhenTwoChargesAreSavedAtTheSameTime()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var appointmentId = await SeedAppointmentAsync(database, tenant.Id, visit: true);
        await using var context = database.CreateContext(tenant.Id);
        var appointment = await context.Appointments.SingleAsync(a => a.Id == appointmentId);

        // Another cashier charges it after this request read it.
        var theirs = await ChargeAsync(database, tenant.Id, appointmentId);

        var order = NewSale(tenant.Id);
        order.ChargeVisit(appointmentId, appointment.Visit!.ZoneName, appointment.Visit.Fee, VatRate.General);
        appointment.RecordSale(order.Id);
        context.Orders.Add(order);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());

        await using var check = database.CreateContext(tenant.Id);
        Assert.Equal(theirs, (await check.Appointments.SingleAsync()).SaleOrderId);
    }

    [SqlServerFact]
    public async Task Charge_ShouldConflict_WithTheVisitMovedMeanwhile()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var appointmentId = await SeedAppointmentAsync(database, tenant.Id, visit: true);
        await using var context = database.CreateContext(tenant.Id);
        var appointment = await context.Appointments.SingleAsync(a => a.Id == appointmentId);

        // Someone moves the visit to an address with another charge after this request read it.
        await using (var meanwhile = database.CreateContext(tenant.Id))
        {
            var moved = await meanwhile.Appointments.SingleAsync(a => a.Id == appointmentId);
            moved.ArrangeVisit(DeliveryAddress.ForVisit("72005", "Calle 5 Sur 300", null), Zone(tenant.Id, fee: 120m), Now.AddMinutes(5));
            await meanwhile.SaveChangesAsync();
        }

        // The charge this request read is not the one agreed now, so the appointment's version refuses it.
        var order = NewSale(tenant.Id);
        order.ChargeVisit(appointmentId, appointment.Visit!.ZoneName, appointment.Visit.Fee, VatRate.General);
        appointment.RecordSale(order.Id);
        context.Orders.Add(order);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());
    }

    [SqlServerFact]
    public async Task Keys_ShouldKeepEachVisitAndChargeOnce_AndTheSalesUnderTheirAppointments()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var appointmentId = await SeedAppointmentAsync(database, tenant.Id, visit: true);
        var orderId = await ChargeAsync(database, tenant.Id, appointmentId);
        await using var context = database.CreateContext();

        // One visit per appointment and one charge per sale.
        var secondVisit = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [AppointmentVisits] ([Id], [TenantId], [AppointmentId], [PostalCode], [AddressLine], [ZoneName], [Fee])
            VALUES ({Guid.NewGuid()}, {tenant.Id}, {appointmentId}, '72005', N'Calle 5 Sur 300', N'Centro', 0)
            """));
        var secondCharge = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [OrderVisitCharges] ([Id], [TenantId], [OrderId], [AppointmentId], [ZoneName], [Fee], [FeeTaxRate])
            VALUES ({Guid.NewGuid()}, {tenant.Id}, {orderId}, {appointmentId}, N'Centro', 80, 16)
            """));
        Assert.Equal((2601, 2601), (secondVisit.Number, secondCharge.Number));

        // A sale that charged an appointment, and an appointment with a charge, stay.
        var orderGone = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [Orders] WHERE [Id] = {orderId}"));
        var appointmentGone = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [Appointments] WHERE [Id] = {appointmentId}"));
        Assert.Equal((547, 547), (orderGone.Number, appointmentGone.Number));

        // An appointment that was never charged takes its visit with it.
        var uncharged = await SeedAppointmentAsync(database, tenant.Id, visit: true);
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [Appointments] WHERE [Id] = {uncharged}");
        Assert.Equal(1, await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [AppointmentVisits]").SingleAsync());
    }

    [SqlServerFact]
    public async Task Checks_ShouldRefuse_ARowTheDomainNeverWrites()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var appointmentId = await SeedAppointmentAsync(database, tenant.Id, visit: false);
        var orderId = await SeedSaleAsync(database, tenant.Id);
        await using var context = database.CreateContext();

        // A visit: a postal code of 5 digits, an address and a charge from $0 to the limit of a zone.
        foreach (var (postalCode, addressLine, fee) in new (string, string, decimal)[]
        {
            ("7200", "Av. Juárez 123", 0m), ("7200a", "Av. Juárez 123", 0m), ("72000", "", 0m), ("72000", "Av. Juárez 123", -1m),
            ("72000", "Av. Juárez 123", 10_000.01m),
        })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [AppointmentVisits] ([Id], [TenantId], [AppointmentId], [PostalCode], [AddressLine], [ZoneName], [Fee])
                VALUES ({Guid.NewGuid()}, {tenant.Id}, {appointmentId}, {postalCode}, {addressLine}, N'Centro', {fee})
                """));
            Assert.True(error.Number == 547, $"({postalCode}, {addressLine}, {fee}): error {error.Number}");
        }

        // A visit charge: something to charge, up to the limit, and a rate of IVA.
        foreach (var (fee, rate) in new (decimal, decimal)[] { (0m, 16m), (10_000.01m, 16m), (80m, -1m), (80m, 100.01m) })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [OrderVisitCharges] ([Id], [TenantId], [OrderId], [AppointmentId], [ZoneName], [Fee], [FeeTaxRate])
                VALUES ({Guid.NewGuid()}, {tenant.Id}, {orderId}, {appointmentId}, N'Centro', {fee}, {rate})
                """));
            Assert.True(error.Number == 547, $"({fee}, {rate}): error {error.Number}");
        }

        // The travel time: from 0 to 4 hours.
        foreach (var minutes in new[] { -1, 241 })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [TenantSettings] SET [HomeVisitTravelMinutes] = {minutes} WHERE [TenantId] = {tenant.Id}"));
            Assert.True(error.Number == 547, $"{minutes}: error {error.Number}");
        }
    }

    [SqlServerFact]
    public async Task TravelMinutes_ShouldBeSaved_StartingAtThirty()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, other) = await SeedAsync(database);

        await using (var context = database.CreateContext(tenant.Id))
        {
            var settings = await context.TenantSettings.SingleAsync();
            Assert.Equal(30, settings.HomeVisitTravelMinutes);
            // 0 is a value of its own, not «use the default».
            settings.SetHomeVisitTravelMinutes(0);
            await context.SaveChangesAsync();
        }

        await using (var check = database.CreateContext(tenant.Id))
            Assert.Equal(0, (await check.TenantSettings.SingleAsync()).HomeVisitTravelMinutes);
        await using var neighbor = database.CreateContext(other.Id);
        Assert.Equal(30, (await neighbor.TenantSettings.SingleAsync()).HomeVisitTravelMinutes);
    }

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepTakingAppointmentsAndSettings_LeavingVisitsAsTheyWere()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var visitId = await SeedAppointmentAsync(database, tenant.Id, visit: true);
        var newTenant = NewTenant("Barbería nueva");

        // The version deployed before (Fase 22D) does not know where an appointment happens nor its sale: it books, moves
        // and confirms appointments, starts the settings of a new business and changes them.
        await using (var context = BeforeHomeVisits.CreateContext(database, tenant.Id))
        {
            context.Appointments.Add(new Appointment(tenant.Id, "Luis", "5587654321", "Corte", Start.AddHours(2), Start.AddHours(3)));
            var visit = await context.Appointments.SingleAsync(a => a.Id == visitId);
            visit.Update(visit.CustomerName, visit.CustomerPhone, visit.ServiceName, Start.AddDays(1), Start.AddDays(1).AddHours(1),
                visit.StaffName, "Llevar toallas");
            visit.ChangeStatus(AppointmentStatus.Confirmed);
            (await context.TenantSettings.SingleAsync()).Update("MXN", "America/Mexico_City", autoAcceptOrders: true, 10);
            await context.SaveChangesAsync();
        }

        await using (var seed = BeforeHomeVisits.CreateContext(database, newTenant.Id))
        {
            seed.AddRange(newTenant, new TenantSettings(newTenant.Id, TenantCapability.Service | TenantCapability.Appointments));
            await seed.SaveChangesAsync();
        }

        await using (var fresh = database.CreateContext(newTenant.Id))
            Assert.Equal(30, (await fresh.TenantSettings.SingleAsync()).HomeVisitTravelMinutes);
        await using var check = database.CreateContext(tenant.Id);
        var appointments = await check.Appointments.ToListAsync();
        Assert.Equal(2, appointments.Count);
        var kept = appointments.Single(a => a.Id == visitId);
        Assert.Equal((AppointmentStatus.Confirmed, "Llevar toallas", "72000", 80m),
            (kept.Status, kept.Notes, kept.Visit!.PostalCode, kept.Visit.Fee));
        Assert.Single(appointments, a => a.Visit is null && a.SaleOrderId is null && a.Version.Length == 8);
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_WhileThereIsAnyVisitOrCharge_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var (tenant, _) = await SeedAsync(database);
        var appointmentId = await SeedAppointmentAsync(database, tenant.Id, visit: true);
        await ChargeAsync(database, tenant.Id, appointmentId);

        // A visit stops it; so does a visit charge alone, and then a charged appointment alone, each deleted on purpose.
        foreach (var cleanUp in new[]
        {
            "DELETE FROM [AppointmentVisits]", "DELETE FROM [OrderVisitCharges]", "UPDATE [Appointments] SET [SaleOrderId] = NULL",
        })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, MigrationBefore));
            Assert.Equal(50015, error.Number);
            Assert.Contains("home visits or charged appointments", error.Message);
            await using var context = database.CreateContext(tenant.Id);
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            await context.Database.ExecuteSqlRawAsync(cleanUp);
        }

        await MigrateAsync(database, MigrationBefore);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(MigrationBefore, await LatestMigrationAsync(context));
            Assert.Equal(0, await context.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] IN (N'AppointmentVisits', N'OrderVisitCharges')
                """).SingleAsync());
            Assert.Equal(0, await context.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS [Value] FROM sys.columns
                WHERE ([object_id] = OBJECT_ID(N'dbo.Appointments') AND [name] IN (N'SaleOrderId', N'Version'))
                    OR ([object_id] = OBJECT_ID(N'dbo.TenantSettings') AND [name] = N'HomeVisitTravelMinutes')
                """).SingleAsync());
            Assert.Equal((1, 1), (
                await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [Appointments]").SingleAsync(),
                await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [Orders]").SingleAsync()));
        }

        // Rolling back again does nothing, and forward again the tables are back, empty, and the settings start at 30.
        await MigrateAsync(database, MigrationBefore);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(tenant.Id);
        Assert.Empty(await forward.AppointmentVisits.ToListAsync());
        Assert.Empty(await forward.OrderVisitCharges.ToListAsync());
        Assert.Null((await forward.Appointments.SingleAsync()).SaleOrderId);
        Assert.Equal(30, (await forward.TenantSettings.SingleAsync()).HomeVisitTravelMinutes);
    }

    /// <summary>An appointment of a haircut, at 72000 by a zone that charges $80 when <paramref name="visit"/>.</summary>
    private static async Task<Guid> SeedAppointmentAsync(SqlServerTestDatabase database, Guid tenantId, bool visit)
    {
        await using var context = database.CreateContext(tenantId);
        var appointment = new Appointment(tenantId, "Ana", "5512345678", "Corte a domicilio", Start, Start.AddHours(1), "Luis");
        if (visit)
            appointment.ArrangeVisit(Address, Zone(tenantId), Now);
        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();
        return appointment.Id;
    }

    /// <summary>The sale of the appointment: the haircut of $400 and its visit charge, saved with the appointment done.</summary>
    private static async Task<Guid> ChargeAsync(SqlServerTestDatabase database, Guid tenantId, Guid appointmentId)
    {
        await using var context = database.CreateContext(tenantId);
        var appointment = await context.Appointments.SingleAsync(a => a.Id == appointmentId);
        var order = NewSale(tenantId);
        order.ChargeVisit(appointmentId, appointment.Visit!.ZoneName, appointment.Visit.Fee, VatRate.General);
        appointment.RecordSale(order.Id);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    private static async Task<Guid> SeedSaleAsync(SqlServerTestDatabase database, Guid tenantId)
    {
        await using var context = database.CreateContext(tenantId);
        var order = NewSale(tenantId);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    private static Order NewSale(Guid tenantId)
    {
        var order = new Order(
            tenantId, null, null, createdByBusiness: true, $"S-{Guid.NewGuid():N}"[..10], BusinessType.Service, OrderType.Service,
            "Ana", customerPhone: "5512345678", customerNotes: null, tableNumber: null, pricesIncludeTax: true,
            channel: OrderChannel.Counter);
        order.AddItem(Guid.NewGuid(), "Corte a domicilio", 1, 400m);
        return order;
    }

    private static DeliveryZone Zone(Guid tenantId, decimal fee = 80m)
        => new(tenantId, new DeliveryZoneFields("Centro", fee, 0m, null, ["72000", "72005"]));

    private static async Task<(Tenant Tenant, Tenant Other)> SeedAsync(SqlServerTestDatabase database)
    {
        var tenant = NewTenant("Barbería de Luis");
        var other = NewTenant("Estética vecina");
        const TenantCapability Capabilities = TenantCapability.Service | TenantCapability.Appointments;
        await using var seed = database.CreateContext();
        seed.AddRange(tenant, other, new TenantSettings(tenant.Id, Capabilities), new TenantSettings(other.Id, Capabilities));
        await seed.SaveChangesAsync();
        return (tenant, other);
    }

    private static Tenant NewTenant(string name)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        return new Tenant(name, slug, BusinessType.Service, $"{slug}@demo.com");
    }

    /// <summary>Applies the migrations up to <paramref name="target"/>, as a deployment does.</summary>
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
