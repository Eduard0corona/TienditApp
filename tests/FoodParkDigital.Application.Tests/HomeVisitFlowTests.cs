using FoodParkDigital.Application.Appointments;
using FoodParkDigital.Application.Appointments.Commands;
using FoodParkDigital.Application.Appointments.DTOs;
using FoodParkDigital.Application.Appointments.HomeVisits;
using FoodParkDigital.Application.Appointments.Queries;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Inventory;
using FoodParkDigital.Application.Messaging;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Appointments at the customer's address through the use cases (Fase 22E): the zone that reaches the postal code sets the
/// charge, kept while the address stays; the travel time between a visit and another appointment of the same person; the
/// notices; the quote, the route of the day and the travel setting; and the sale that charges the appointment, with the
/// visit as one more concept of the total and of the invoice.
/// </summary>
public class HomeVisitFlowTests
{
    // 00:00 UTC of the fixture's day; 16:00 UTC is 10:00 in Mexico City, the business's time zone.
    private static readonly DateTimeOffset Today = new(SalesTestFixture.Now.UtcDateTime.Date, TimeSpan.Zero);
    private static readonly DateTimeOffset Tomorrow10 = Today.AddDays(1).AddHours(16);
    private static readonly AppointmentVisitInput AtHome = new()
    {
        PostalCode = "72000",
        AddressLine = "Av. Juárez 123, Col. Centro",
        References = "Portón negro"
    };

    [Fact]
    public async Task Visit_ShouldTakeTheChargeOfTheZone_AndKeepTheAddressOutOfTheActivity()
    {
        await using var fixture = SalesTestFixture.Create();
        await SalonWithZonesAsync(fixture);

        var created = await CreateAsync(fixture, Appointment() with { Visit = AtHome });

        Assert.True(created.IsSuccess, created.Error);
        var visit = created.Value!.Visit!;
        Assert.Equal(("72000", "Av. Juárez 123, Col. Centro", "Portón negro", "Centro", 80m),
            (visit.PostalCode, visit.AddressLine, visit.References, visit.ZoneName, visit.Fee));
        var entry = await fixture.Context.AuditLogs.SingleAsync(a => a.Action == AppointmentVisitAudit.Arranged);
        Assert.Equal(created.Value.Id.ToString(), entry.EntityId);
        Assert.Contains("Centro", entry.Metadata);
        Assert.DoesNotContain("Juárez", entry.Metadata);
        Assert.DoesNotContain("72000", entry.Metadata);
    }

    [Fact]
    public async Task Visit_WithoutZones_OrOutsideThem_ShouldSayWhy_AndBookNothing()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await SalonAsync(fixture);

        var withoutZones = await CreateAsync(fixture, Appointment() with { Visit = AtHome });
        await AddZoneAsync(fixture, tenant, "Sur", 50m, isActive: true, "72500");
        var outside = await CreateAsync(fixture, Appointment() with { Visit = AtHome });
        var badAddress = await CreateAsync(fixture, Appointment() with { Visit = AtHome with { PostalCode = "7200" } });

        Assert.Equal(HomeVisitRules.NoZonesMessage, withoutZones.Error);
        Assert.Equal(HomeVisitRules.NotCoveredMessage("72000"), outside.Error);
        Assert.Equal(DeliveryZone.NotAPostalCodeMessage("7200"), badAddress.Error);
        Assert.Equal(0, await fixture.Context.Appointments.CountAsync());
    }

    [Fact]
    public async Task Visit_AtTheSameAddress_ShouldKeepTheChargeAgreed_AnotherAddressShouldQuoteAgain_AndNoneShouldDropIt()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await SalonWithZonesAsync(fixture);
        var created = (await CreateAsync(fixture, Appointment() with { Visit = AtHome })).Value!;
        var centro = await fixture.Context.DeliveryZones.SingleAsync(z => z.Name == "Centro");
        centro.Update(new DeliveryZoneFields("Centro", 120m, 0m, null, ["72000", "72005"]));
        await fixture.Context.SaveChangesAsync();

        var sameAddress = await UpdateAsync(fixture, Edit(created) with { Notes = "Llevar toallas", Visit = AtHome });
        // The same place with other references keeps the charge agreed: nothing is quoted again.
        var otherReferences = await UpdateAsync(fixture, Edit(created) with { Visit = AtHome with { References = "Timbre 6" } });
        var otherAddress = await UpdateAsync(fixture, Edit(created) with { Visit = AtHome with { PostalCode = "72005", References = null } });
        var atTheBusiness = await UpdateAsync(fixture, Edit(created) with { Visit = null });

        Assert.Equal(80m, sameAddress.Value!.Visit!.Fee);
        Assert.Equal(("Timbre 6", 80m), (otherReferences.Value!.Visit!.References, otherReferences.Value.Visit.Fee));
        Assert.Equal(("72005", 120m), (otherAddress.Value!.Visit!.PostalCode, otherAddress.Value.Visit.Fee));
        Assert.Null(atTheBusiness.Value!.Visit);
        Assert.Equal(0, await fixture.Context.AppointmentVisits.CountAsync());
        // Booked at home, its references changed, moved to another address and then to the business: the same address again
        // changed nothing.
        Assert.Equal(
            [AppointmentVisitAudit.Arranged, AppointmentVisitAudit.Arranged, AppointmentVisitAudit.Arranged, AppointmentVisitAudit.Removed],
            (await fixture.Context.AuditLogs.Where(a => a.EntityName == "Appointment").Select(a => a.Action).ToListAsync()).Order());
        Assert.Equal(tenant.Id, (await fixture.Context.Appointments.SingleAsync()).TenantId);
    }

    [Fact]
    public async Task Travel_ShouldKeepTheTimeOfTheBusiness_BetweenAVisitAndAnotherAppointmentOfTheSamePerson()
    {
        await using var fixture = SalesTestFixture.Create();
        await SalonWithZonesAsync(fixture);
        Assert.True((await CreateAsync(fixture, Appointment(Tomorrow10, staff: "Luis") with { Visit = AtHome })).IsSuccess);

        var tooSoon = await CreateAsync(fixture, Appointment(Tomorrow10.AddMinutes(75), staff: "Luis"));
        var tooSoonBefore = await CreateAsync(fixture, Appointment(Tomorrow10.AddMinutes(-80), staff: "Luis"));
        var withTime = await CreateAsync(fixture, Appointment(Tomorrow10.AddMinutes(90), staff: "Luis"));
        var someoneElse = await CreateAsync(fixture, Appointment(Tomorrow10.AddHours(1), staff: "Ana"));
        // Two appointments at the business need no trip between them.
        var backToBack = await CreateAsync(fixture, Appointment(Tomorrow10.AddHours(2), staff: "Ana"));

        Assert.Equal(HomeVisitRules.TravelMessage("Luis", 30), tooSoon.Error);
        Assert.Equal(HomeVisitRules.TravelMessage("Luis", 30), tooSoonBefore.Error);
        Assert.True(withTime.IsSuccess, withTime.Error);
        Assert.True(someoneElse.IsSuccess, someoneElse.Error);
        Assert.True(backToBack.IsSuccess, backToBack.Error);

        // A visit right after an appointment at the business needs the trip too, unless the business sets none.
        var visitTooSoon = await CreateAsync(fixture, Appointment(Tomorrow10.AddHours(3), staff: "Ana") with { Visit = AtHome });
        Assert.Equal(HomeVisitRules.TravelMessage("Ana", 30), visitTooSoon.Error);
        await new UpdateHomeVisitSettingsCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser)
            .Handle(new UpdateHomeVisitSettingsCommand(0), CancellationToken.None);
        var noTrip = await CreateAsync(fixture, Appointment(Tomorrow10.AddHours(3), staff: "Ana") with { Visit = AtHome });
        Assert.True(noTrip.IsSuccess, noTrip.Error);
    }

    [Fact]
    public async Task Notices_ShouldSayAtHomeWithThePostalCode_AndTheCharge_NeverTheAddress()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await SalonWithZonesAsync(fixture, pricesIncludeTax: true);
        await ConnectWhatsAppAsync(fixture, tenant.Id);

        var created = (await CreateAsync(fixture, Appointment() with { Visit = AtHome })).Value!;
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(1);
        // Same time, at the business now: the customer hears of the new place.
        await UpdateAsync(fixture, Edit(created) with { Visit = null });

        var notices = await fixture.Context.OutgoingMessages
            .AsNoTracking()
            .Where(m => m.Purpose == MessagePurpose.AppointmentNotice)
            .OrderBy(m => m.NextAttemptAt)
            .Select(m => m.Body)
            .ToListAsync();
        Assert.Equal(
        [
            "Hola Luis, quedó agendada tu cita de Corte el lunes 5 de octubre a las 10:00 a. m. a domicilio (CP 72000) con "
                + "Negocio demo. Cargo por visita: $80.00.",
            "Hola Luis, tu cita de Corte ahora es el lunes 5 de octubre a las 10:00 a. m. en Negocio demo."
        ], notices);
        Assert.DoesNotContain(notices, body => body.Contains("Juárez"));
    }

    [Fact]
    public async Task Reminder_OfAVisit_ShouldSayAtHomeWithThePostalCode_NeverTheAddress()
    {
        await using var fixture = SalesTestFixture.Create();
        await SalonWithZonesAsync(fixture, pricesIncludeTax: true);
        await CreateAsync(fixture, Appointment() with { Visit = AtHome });
        var settings = await fixture.Context.TenantSettings.SingleAsync();
        settings.UpdateAppointmentReminders(enabled: true, hoursBefore: 24, templateId: null);
        await fixture.Context.SaveChangesAsync();

        await new AppointmentReminderPlanner(fixture.Context, new PhoneNormalizationService(), fixture.Clock)
            .QueueDueAsync(settings, CancellationToken.None);
        await fixture.Context.SaveChangesAsync();

        var reminder = await fixture.Context.OutgoingMessages.SingleAsync(m => m.Purpose == MessagePurpose.AppointmentReminder);
        Assert.Equal(
            "Hola Luis, te recordamos tu cita de Corte el lunes 5 de octubre a las 10:00 a. m. a domicilio (CP 72000) con "
                + "Negocio demo. Si necesitas cambiarla, respóndenos este mensaje. Escribe BAJA si no quieres recibir recordatorios.",
            reminder.Body);
    }

    [Fact]
    public async Task Notice_OfABusinessWithPricesBeforeIva_ShouldSayTheChargeIsPlusIva()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await SalonWithZonesAsync(fixture, pricesIncludeTax: false);
        await ConnectWhatsAppAsync(fixture, tenant.Id);

        await CreateAsync(fixture, Appointment() with { Visit = AtHome });

        var notice = await fixture.Context.OutgoingMessages.SingleAsync();
        Assert.EndsWith("Cargo por visita: $80.00 más IVA.", notice.Body);
    }

    [Fact]
    public async Task Quote_ShouldSayTheZoneAndTheCharge_OrWhyNot()
    {
        await using var fixture = SalesTestFixture.Create();
        await SalonWithZonesAsync(fixture);
        var handler = new GetVisitQuoteQueryHandler(fixture.Context, fixture.TenantService);

        var covered = await handler.Handle(new GetVisitQuoteQuery(" 72005 "), CancellationToken.None);
        var outside = await handler.Handle(new GetVisitQuoteQuery("75000"), CancellationToken.None);
        var invalid = new GetVisitQuoteQueryValidator().Validate(new GetVisitQuoteQuery("7200"));

        Assert.Equal(("72005", "Centro", 80m), (covered.Value!.PostalCode, covered.Value.ZoneName, covered.Value.Fee));
        Assert.Equal(HomeVisitRules.NotCoveredMessage("75000"), outside.Error);
        Assert.Equal(HomeVisitPlacement.PostalCodeMessage, Assert.Single(invalid.Errors).ErrorMessage);
    }

    [Fact]
    public async Task Route_ShouldListTheVisitsOfTheDay_ByPersonInTheOrderOfTheirTime()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await SalonWithZonesAsync(fixture);
        var luis = await AddMemberAsync(fixture, tenant.Id, "Luis", "Paz");
        var ana = await AddMemberAsync(fixture, tenant.Id, "Ana", "Ruiz");
        var noon = await CreateAsync(fixture, Appointment(Tomorrow10.AddHours(2)) with { StaffUserId = luis, Visit = AtHome with { PostalCode = "72005" } });
        var ten = await CreateAsync(fixture, Appointment(Tomorrow10) with { StaffUserId = luis, Visit = AtHome });
        await CreateAsync(fixture, Appointment(Tomorrow10.AddHours(1)) with { StaffUserId = ana, Visit = AtHome });
        await CreateAsync(fixture, Appointment(Tomorrow10.AddHours(5)) with { StaffUserId = luis });
        await CreateAsync(fixture, Appointment(Tomorrow10.AddHours(6)) with { Visit = AtHome });
        var cancelled = await CreateAsync(fixture, Appointment(Tomorrow10.AddHours(7)) with { StaffUserId = luis, Visit = AtHome });
        await new UpdateAppointmentStatusCommandHandler(fixture.Context, Notices(fixture))
            .Handle(new UpdateAppointmentStatusCommand { AppointmentId = cancelled.Value!.Id, NewStatus = AppointmentStatus.Cancelled }, CancellationToken.None);
        await CreateAsync(fixture, Appointment(Tomorrow10.AddDays(1)) with { StaffUserId = luis, Visit = AtHome });
        var handler = new GetVisitRouteQueryHandler(fixture.Context, fixture.TenantService);

        var day = await handler.Handle(new GetVisitRouteQuery(new DateOnly(2026, 10, 5)), CancellationToken.None);
        var onlyLuis = await handler.Handle(new GetVisitRouteQuery(new DateOnly(2026, 10, 5), luis), CancellationToken.None);

        Assert.Equal(["Ana Ruiz", "Luis Paz", null], day.Staff.Select(s => s.StaffName));
        var luisRoute = day.Staff[1];
        Assert.Equal([ten.Value!.Id, noon.Value!.Id], luisRoute.Stops.Select(s => s.AppointmentId));
        Assert.Equal((160m, "72000", "Portón negro"), (luisRoute.FeeTotal, luisRoute.Stops[0].PostalCode, luisRoute.Stops[0].References));
        Assert.Equal(luis, Assert.Single(onlyLuis.Staff).StaffUserId);
        // A day the agenda can hold: missing or at the ends of the calendar, it is refused before reading anything.
        var validator = new GetVisitRouteQueryValidator();
        Assert.Equal(GetVisitRouteQueryValidator.DateMessage,
            Assert.Single(validator.Validate(new GetVisitRouteQuery(default)).Errors).ErrorMessage);
        Assert.False(validator.Validate(new GetVisitRouteQuery(DateOnly.MaxValue)).IsValid);
        Assert.True(validator.Validate(new GetVisitRouteQuery(new DateOnly(2026, 10, 5))).IsValid);
    }

    [Fact]
    public async Task TravelMinutes_ShouldBeReadByTheTeam_AndSetByTheOwner_WithTheirActivity()
    {
        await using var fixture = SalesTestFixture.Create();
        await SalonAsync(fixture);
        var read = new GetHomeVisitSettingsQueryHandler(fixture.Context, fixture.TenantService);
        var update = new UpdateHomeVisitSettingsCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser);

        Assert.Equal(30, (await read.Handle(new GetHomeVisitSettingsQuery(), CancellationToken.None)).TravelMinutes);
        Assert.Equal(45, (await update.Handle(new UpdateHomeVisitSettingsCommand(45), CancellationToken.None)).Value!.TravelMinutes);
        await update.Handle(new UpdateHomeVisitSettingsCommand(45), CancellationToken.None);

        Assert.Equal(45, (await read.Handle(new GetHomeVisitSettingsQuery(), CancellationToken.None)).TravelMinutes);
        Assert.Equal(1, await fixture.Context.AuditLogs.CountAsync(a => a.Action == "HomeVisitTravelMinutesChanged"));
        Assert.Equal(HomeVisitRules.TravelMinutesMessage,
            Assert.Single(new UpdateHomeVisitSettingsCommandValidator().Validate(new UpdateHomeVisitSettingsCommand(241)).Errors).ErrorMessage);
    }

    [Fact]
    public async Task Sale_ShouldChargeTheVisit_AndCompleteTheAppointment_OnceUnlessItsSaleWasCancelled()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await SalonWithZonesAsync(fixture, pricesIncludeTax: true);
        var haircut = await fixture.AddProductAsync(tenant, "Corte a domicilio", 400m, unit: UnitType.Service);
        var appointment = (await CreateAsync(fixture, Appointment() with { Visit = AtHome })).Value!;

        var sale = await SellAsync(fixture, Sale(haircut, appointment.Id) with { ExpectedTotal = 480m });

        Assert.True(sale.IsSuccess, sale.Error);
        Assert.Equal(480m, sale.Value!.Total);
        Assert.Equal((appointment.Id, "Centro", 80m, 16m),
            (sale.Value.VisitCharge!.AppointmentId, sale.Value.VisitCharge.ZoneName, sale.Value.VisitCharge.Fee, sale.Value.VisitCharge.FeeTaxRate));
        Assert.Equal(480m, sale.Value.TaxBreakdown.Sum(line => line.Total));
        var charged = await fixture.Context.Appointments.AsNoTracking().SingleAsync();
        Assert.Equal((sale.Value.Id, AppointmentStatus.Completed), (charged.SaleOrderId, charged.Status));

        var again = await SellAsync(fixture, Sale(haircut, appointment.Id));
        Assert.Equal(HomeVisitRules.AlreadySoldMessage, again.Error);
        var read = new GetAppointmentByIdQueryHandler(fixture.Context);
        Assert.False((await read.Handle(new GetAppointmentByIdQuery(appointment.Id), CancellationToken.None)).SaleCancelled);

        await new CancelOrderCommandHandler(fixture.Context, fixture.CurrentUser, Inventory(fixture))
            .Handle(new CancelOrderCommand { OrderId = sale.Value.Id, Reason = "Se cobró mal" }, CancellationToken.None);
        // Its sale was cancelled: the agenda and the route offer «Cobrar» again.
        Assert.True((await read.Handle(new GetAppointmentByIdQuery(appointment.Id), CancellationToken.None)).SaleCancelled);
        var list = await new GetAppointmentsQueryHandler(fixture.Context).Handle(new GetAppointmentsQuery(), CancellationToken.None);
        Assert.True(Assert.Single(list.Items).SaleCancelled);
        var route = await new GetVisitRouteQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new GetVisitRouteQuery(new DateOnly(2026, 10, 5)), CancellationToken.None);
        Assert.True(Assert.Single(Assert.Single(route.Staff).Stops).SaleCancelled);
        var resold = await SellAsync(fixture, Sale(haircut, appointment.Id));
        Assert.True(resold.IsSuccess, resold.Error);
        Assert.Equal(resold.Value!.Id, (await fixture.Context.Appointments.AsNoTracking().SingleAsync()).SaleOrderId);
        Assert.False((await read.Handle(new GetAppointmentByIdQuery(appointment.Id), CancellationToken.None)).SaleCancelled);
        Assert.Equal(2, await fixture.Context.OrderVisitCharges.CountAsync());
    }

    [Fact]
    public async Task Sale_OfAnAppointmentAtTheBusiness_OrAFreeVisit_ShouldOnlyCompleteIt()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await SalonWithZonesAsync(fixture, pricesIncludeTax: true);
        await AddZoneAsync(fixture, tenant, "Norte", 0m, isActive: true, "72100");
        var haircut = await fixture.AddProductAsync(tenant, "Corte", 400m, unit: UnitType.Service);
        var atTheBusiness = (await CreateAsync(fixture, Appointment())).Value!;
        var free = (await CreateAsync(fixture, Appointment(Tomorrow10.AddHours(3)) with { Visit = AtHome with { PostalCode = "72100" } })).Value!;

        var first = await SellAsync(fixture, Sale(haircut, atTheBusiness.Id));
        var second = await SellAsync(fixture, Sale(haircut, free.Id));

        Assert.Equal((400m, (OrderVisitChargeDto?)null), (first.Value!.Total, first.Value.VisitCharge));
        Assert.Equal((400m, (OrderVisitChargeDto?)null), (second.Value!.Total, second.Value.VisitCharge));
        Assert.All(await fixture.Context.Appointments.AsNoTracking().ToListAsync(), a => Assert.Equal(AppointmentStatus.Completed, a.Status));
    }

    [Fact]
    public async Task Sale_ShouldRefuseAnAppointmentCancelled_Missed_OrOfAnotherBusiness_AndSellNothing()
    {
        await using var fixture = SalesTestFixture.Create();
        var other = await SalonAsync(fixture, slug: "otro");
        var foreign = (await CreateAsync(fixture, Appointment())).Value!;
        var tenant = await SalonWithZonesAsync(fixture, slug: "propio");
        var haircut = await fixture.AddProductAsync(tenant, "Corte", 400m, unit: UnitType.Service);
        var cancelled = (await CreateAsync(fixture, Appointment())).Value!;
        var missed = (await CreateAsync(fixture, Appointment(Tomorrow10.AddHours(3)))).Value!;
        var status = new UpdateAppointmentStatusCommandHandler(fixture.Context, Notices(fixture));
        await status.Handle(new UpdateAppointmentStatusCommand { AppointmentId = cancelled.Id, NewStatus = AppointmentStatus.Cancelled }, CancellationToken.None);
        await status.Handle(new UpdateAppointmentStatusCommand { AppointmentId = missed.Id, NewStatus = AppointmentStatus.NoShow }, CancellationToken.None);

        Assert.Equal(HomeVisitRules.CancelledSaleMessage, (await SellAsync(fixture, Sale(haircut, cancelled.Id))).Error);
        Assert.Equal(HomeVisitRules.NoShowSaleMessage, (await SellAsync(fixture, Sale(haircut, missed.Id))).Error);
        Assert.Equal(CreateOrderCommandValidator.AppointmentNotFoundMessage, (await SellAsync(fixture, Sale(haircut, foreign.Id))).Error);
        Assert.Equal(0, await fixture.Context.Orders.CountAsync());
        Assert.NotEqual(tenant.Id, other.Id);
    }

    [Fact]
    public void SaleOfAnAppointment_ShouldBeACounterSaleOfTheBusiness_WithConnection()
    {
        var validator = new CreateOrderCommandValidator();
        // Products too (a service would also be refused for going to an address).
        var sale = new CreateOrderCommand
        {
            OrderType = OrderType.Retail,
            Items = [new CreateOrderItemDto { ProductId = Guid.NewGuid(), Quantity = 1 }],
            AppointmentId = Guid.NewGuid()
        };

        string Error(CreateOrderCommand command) => Assert.Single(validator.Validate(command).Errors).ErrorMessage;

        Assert.True(validator.Validate(sale).IsValid);
        Assert.Equal(CreateOrderCommandValidator.AppointmentSaleMessage,
            Error(sale with { Delivery = new OrderDeliveryInput { PostalCode = "72000", AddressLine = "Av. Juárez 123" } }));
        // An order the customer places needs more than that (their phone, the service detail); this is one of its errors.
        Assert.Contains(CreateOrderCommandValidator.AppointmentSaleMessage,
            validator.Validate(sale with { CreatedByBusiness = false }).Errors.Select(e => e.ErrorMessage));
        Assert.Equal(CreateOrderCommandValidator.AppointmentWithConnectionMessage,
            Error(sale with { IdempotencyKey = Guid.NewGuid(), CapturedOfflineAt = SalesTestFixture.Now }));
    }

    [Fact]
    public async Task CfdiDraft_ShouldInvoiceTheVisit_WithTheCodeOfTheServiceSold_AsItsOwnConcept()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await SalonWithZonesAsync(fixture, pricesIncludeTax: true);
        var haircut = await fixture.AddProductAsync(tenant, "Corte a domicilio", 400m, unit: UnitType.Service);
        haircut.SetFiscalData("86101710", "E48", CfdiTaxObject.Subject);
        await fixture.Context.SaveChangesAsync();
        var appointment = (await CreateAsync(fixture, Appointment() with { Visit = AtHome })).Value!;
        var sale = await SellAsync(fixture, Sale(haircut, appointment.Id));

        var draft = await new GetOrderCfdiDraftQueryHandler(fixture.Context)
            .Handle(new GetOrderCfdiDraftQuery { OrderId = sale.Value!.Id }, CancellationToken.None);

        var visit = Assert.Single(draft.Value!.Concepts, concept => concept.IsVisitCharge);
        // $80 with IVA is $68.97 before it; the cent of rounding goes to it so that the invoice adds up to the sale.
        Assert.Equal(("86101710", SatCatalog.ServiceUnitCode, GetOrderCfdiDraftQueryHandler.VisitChargeDescription, 68.96m),
            (visit.ProductCode, visit.UnitCode, visit.Description, visit.Amount));
        Assert.Equal(sale.Value.Total, draft.Value.Total);
        Assert.Contains(GetOrderCfdiDraftQueryHandler.VisitChargeWarning("86101710"), draft.Value.Warnings);
    }

    private static CreateAppointmentCommand Appointment(DateTimeOffset? startAt = null, string? staff = null) => new()
    {
        CustomerName = "Luis",
        CustomerPhone = "5512345678",
        ServiceName = "Corte",
        StaffName = staff,
        StartAt = startAt ?? Tomorrow10,
        EndAt = (startAt ?? Tomorrow10).AddHours(1)
    };

    private static UpdateAppointmentCommand Edit(AppointmentDto appointment) => new()
    {
        AppointmentId = appointment.Id,
        CustomerName = appointment.CustomerName,
        CustomerPhone = appointment.CustomerPhone,
        ServiceName = appointment.ServiceName,
        StartAt = appointment.StartAt,
        EndAt = appointment.EndAt
    };

    private static CreateOrderCommand Sale(Product service, Guid appointmentId) => new()
    {
        OrderType = OrderType.Service,
        CustomerName = "Luis",
        Items = [new CreateOrderItemDto { ProductId = service.Id, Quantity = 1 }],
        AppointmentId = appointmentId
    };

    private static Task<Result<AppointmentDto>> CreateAsync(SalesTestFixture fixture, CreateAppointmentCommand command)
        => new CreateAppointmentCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock, Notices(fixture))
            .Handle(command, CancellationToken.None);

    private static Task<Result<AppointmentDto>> UpdateAsync(SalesTestFixture fixture, UpdateAppointmentCommand command)
        => new UpdateAppointmentCommandHandler(fixture.Context, fixture.CurrentUser, fixture.Clock, Notices(fixture))
            .Handle(command, CancellationToken.None);

    private static async Task<Result<OrderDto>> SellAsync(SalesTestFixture fixture, CreateOrderCommand command)
    {
        var result = await fixture.CreateOrderHandler(Inventory(fixture)).Handle(command, CancellationToken.None);
        // A refused sale is never saved: the next one starts clean, as a new request would.
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static AppointmentNotices Notices(SalesTestFixture fixture)
        => new(fixture.Context, new PhoneNormalizationService(), fixture.Clock);

    private static OrderInventoryService Inventory(SalesTestFixture fixture)
        => new(fixture.Context, fixture.Clock, new FolioGenerator(fixture.Context), NullLogger<OrderInventoryService>.Instance);

    /// <summary>A salon (services and agenda) with the zone «Centro»: 72000 and 72005, $80 to go there.</summary>
    private static async Task<Tenant> SalonWithZonesAsync(SalesTestFixture fixture, bool pricesIncludeTax = false, string slug = "demo")
    {
        var tenant = await SalonAsync(fixture, pricesIncludeTax, slug);
        await AddZoneAsync(fixture, tenant, "Centro", 80m, isActive: true, "72000", "72005");
        return tenant;
    }

    private static Task<Tenant> SalonAsync(SalesTestFixture fixture, bool pricesIncludeTax = false, string slug = "demo")
        => fixture.AddTenantAsync(
            BusinessType.Service, TenantCapability.Service | TenantCapability.Retail | TenantCapability.Appointments,
            pricesIncludeTax: pricesIncludeTax, slug: slug);

    private static async Task AddZoneAsync(
        SalesTestFixture fixture, Tenant tenant, string name, decimal fee, bool isActive, params string[] postalCodes)
    {
        fixture.Context.DeliveryZones.Add(new DeliveryZone(tenant.Id, new DeliveryZoneFields(name, fee, 0m, null, postalCodes, isActive)));
        await fixture.Context.SaveChangesAsync();
    }

    private static async Task ConnectWhatsAppAsync(SalesTestFixture fixture, Guid tenantId)
    {
        var config = new WhatsAppConfig(tenantId);
        config.Update(true, "token", "123456", "waba", "+52 55 0000 0000");
        fixture.Context.WhatsAppConfigs.Add(config);
        await fixture.Context.SaveChangesAsync();
    }

    private static async Task<Guid> AddMemberAsync(SalesTestFixture fixture, Guid tenantId, string firstName, string lastName)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = $"{firstName.ToLowerInvariant()}@negocio.test",
            UserName = $"{firstName.ToLowerInvariant()}@negocio.test",
            FirstName = firstName,
            LastName = lastName,
            Role = UserRole.Staff,
            IsActive = true
        };
        fixture.Context.Users.Add(user);
        fixture.Context.TenantUsers.Add(new TenantUser(tenantId, user.Id, TenantUserRole.Staff));
        await fixture.Context.SaveChangesAsync();
        return user.Id;
    }
}
