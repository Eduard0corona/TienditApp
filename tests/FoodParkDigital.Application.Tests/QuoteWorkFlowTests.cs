using FoodParkDigital.Application.Appointments.HomeVisits;
using FoodParkDigital.Application.Appointments.Queries;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Quotes;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Shop = FoodParkDigital.Application.Tests.QuoteConversionFlowTests.Shop;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Agendar the job of the service order an accepted quote became (Fase 29F, ADR 0053 point 17): an appointment of the team at
/// the place of the visit, with whom and when, charged in its order (never on its own, nor quoted), told to the customer like
/// any appointment, and seen from the quote, the order, the agenda and the route.
/// </summary>
public class QuoteWorkFlowTests
{
    // A week after the visit, at 10:00 in Mexico City.
    private static readonly DateTimeOffset JobStart = SalesTestFixture.Now.AddDays(7).Date.AddHours(16) is var day
        ? new DateTimeOffset(day, TimeSpan.Zero)
        : default;

    [Fact]
    public async Task TheJobOfAQuoteFromAVisit_ShouldBeAgendado_AtThePlaceOfTheVisit_WithNothingToChargeToGoThere()
    {
        await using var shop = await Shop.OpenAsync(pricesIncludeTax: false, policy: HomeVisitChargePolicy.CreditedIfHired);
        var (_, quote) = await shop.CreditedVisitQuoteAsync();
        var order = (await shop.ConvertAsync(quote.Id)).Value!.Order!;
        var luis = await shop.StaffAsync("Luis");

        var scheduled = await shop.ScheduleWorkAsync(quote.Id, JobStart, JobStart.AddHours(3), luis, "Llevar la escalera");

        Assert.True(scheduled.IsSuccess, scheduled.Error);
        var work = Assert.Single(scheduled.Value!.Order!.WorkAppointments);
        Assert.Equal((JobStart, JobStart.AddHours(3), "Luis Pérez", AppointmentStatus.Scheduled, true),
            (work.StartAt, work.EndAt, work.StaffName, work.Status, work.AtCustomersAddress));
        Assert.True(scheduled.Value.CanScheduleWork);

        var appointment = await shop.Context.Appointments.AsNoTracking().SingleAsync(a => a.Id == work.Id);
        Assert.Equal((order.Id, $"Trabajo de la cotización {quote.Number}", "Valeria Ruiz", "+525522107765", "Llevar la escalera"),
            (appointment.WorkOrderId, appointment.ServiceName, appointment.CustomerName, appointment.CustomerPhone, appointment.Notes));
        Assert.Equal((luis, AppointmentChannel.Business, (Guid?)null), (appointment.StaffUserId, appointment.Channel, appointment.SaleOrderId));
        Assert.Equal(("72000", "Av. Juárez 123, Col. Centro", 0m, (HomeVisitChargePolicy?)null),
            (appointment.Visit!.PostalCode, appointment.Visit.AddressLine, appointment.Visit.Fee, appointment.Visit.ChargePolicy));

        // The activity keeps the quote, its order and the appointment, never the customer's data.
        var entry = await shop.Context.AuditLogs.AsNoTracking().SingleAsync(l => l.Action == "QuoteWorkScheduled");
        Assert.Contains(order.Id.ToString(), entry.Metadata);
        Assert.Contains(appointment.Id.ToString(), entry.Metadata);
        Assert.DoesNotContain("Valeria", entry.Metadata);
        Assert.DoesNotContain("Juárez", entry.Metadata);
    }

    [Fact]
    public async Task AQuoteWithoutAVisit_ShouldHaveItsJobAgendado_WithoutAnAddress_AndInMoreThanOneAppointment()
    {
        await using var shop = await Shop.OpenAsync();
        var quote = await shop.AcceptedAsync(new QuoteLineRequest { Description = "Pintar la fachada", Quantity = 1m, UnitPrice = 9000m });
        Assert.True((await shop.ConvertAsync(quote.Id)).IsSuccess);

        Assert.True((await shop.ScheduleWorkAsync(quote.Id, JobStart, JobStart.AddHours(8))).IsSuccess);
        var twoDays = await shop.ScheduleWorkAsync(quote.Id, JobStart.AddDays(1), JobStart.AddDays(1).AddHours(8));

        Assert.True(twoDays.IsSuccess, twoDays.Error);
        Assert.Equal([JobStart, JobStart.AddDays(1)], twoDays.Value!.Order!.WorkAppointments.Select(a => a.StartAt));
        Assert.All(twoDays.Value.Order.WorkAppointments, a => Assert.False(a.AtCustomersAddress));
        var appointments = await shop.Context.Appointments.AsNoTracking().ToListAsync();
        Assert.All(appointments, a => Assert.Null(a.Visit));
        // The order's customer, with the phone the conversion read.
        Assert.All(appointments, a => Assert.Equal(("Valeria Ruiz", "+525522107765"), (a.CustomerName, a.CustomerPhone)));
    }

    [Fact]
    public async Task OnlyTheJobOfAnOrderThatGoesOn_ShouldBeAgendado()
    {
        await using var shop = await Shop.OpenAsync();
        var notConverted = await shop.AcceptedAsync(new QuoteLineRequest { Description = "Revisión", Quantity = 1m, UnitPrice = 500m });
        var cancelled = await shop.AcceptedAsync(new QuoteLineRequest { Description = "Cambio de calentador", Quantity = 1m, UnitPrice = 1160m });
        var order = (await shop.ConvertAsync(cancelled.Id)).Value!.Order!;
        var stored = await shop.Context.Orders.SingleAsync(o => o.Id == order.Id);
        stored.Cancel(OrderStateMachineFactory.Create(stored.BusinessType, stored.OrderType), "caja", "El cliente se arrepintió");
        await shop.Context.SaveChangesAsync();
        shop.Context.ChangeTracker.Clear();

        Assert.Equal(QuoteWork.NotConvertedMessage, (await shop.ScheduleWorkAsync(notConverted.Id, JobStart, JobStart.AddHours(1))).Error);
        Assert.Equal(QuoteWork.OrderCancelledMessage(order.OrderNumber),
            (await shop.ScheduleWorkAsync(cancelled.Id, JobStart, JobStart.AddHours(1))).Error);
        Assert.False((await shop.GetAsync(notConverted.Id)).CanScheduleWork);
        Assert.False((await shop.GetAsync(cancelled.Id)).CanScheduleWork);
        Assert.Empty(await shop.Context.Appointments.ToListAsync());
    }

    [Fact]
    public async Task ABusinessWithoutTheAgenda_ShouldNotBeOfferedToAgendarTheJob()
    {
        await using var shop = await Shop.OpenAsync(TenantCapability.Service | TenantCapability.Retail);
        var quote = await shop.AcceptedAsync(new QuoteLineRequest { Description = "Revisión", Quantity = 1m, UnitPrice = 500m });

        var converted = await shop.ConvertAsync(quote.Id);

        Assert.True(converted.IsSuccess, converted.Error);
        Assert.False(converted.Value!.CanScheduleWork);
    }

    [Fact]
    public async Task TheJob_ShouldRespectTheTimeOfItsPerson_AndTheTravelBetweenVisits()
    {
        await using var shop = await Shop.OpenAsync(pricesIncludeTax: false, policy: HomeVisitChargePolicy.CreditedIfHired);
        var (_, quote) = await shop.CreditedVisitQuoteAsync();
        Assert.True((await shop.ConvertAsync(quote.Id)).IsSuccess);
        var luis = await shop.StaffAsync("Luis");
        var service = await shop.Fixture.AddProductAsync(shop.Tenant, "Revisión", 300m, unit: UnitType.Service);
        // Luis has another visit that ends at the job's start: no time to travel between both (30 minutes).
        await shop.ScheduledVisitAsync(service.Id, JobStart.AddHours(-1), luis);

        Assert.True((await shop.ScheduleWorkAsync(quote.Id, JobStart.AddHours(2), JobStart.AddHours(4), luis)).IsSuccess);
        var overlapping = await shop.ScheduleWorkAsync(quote.Id, JobStart.AddHours(3), JobStart.AddHours(5), luis);
        var noTravel = await shop.ScheduleWorkAsync(quote.Id, JobStart, JobStart.AddHours(1), luis);

        Assert.Equal(QuoteWork.OverlapMessage, overlapping.Error);
        Assert.Equal(HomeVisitRules.TravelMessage("Luis Pérez", HomeVisitRules.DefaultTravelMinutes), noTravel.Error);
    }

    [Fact]
    public async Task OnlySomeoneOfTheTeam_ShouldDoTheJob()
    {
        await using var shop = await Shop.OpenAsync();
        var quote = await shop.AcceptedAsync(new QuoteLineRequest { Description = "Revisión", Quantity = 1m, UnitPrice = 500m });
        Assert.True((await shop.ConvertAsync(quote.Id)).IsSuccess);
        var other = await shop.Fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments, slug: "otro");
        var theirs = await shop.StaffAsync("Pedro", other.Id);
        shop.Fixture.TenantService.SetTenant(shop.Tenant.Id);

        var refused = await shop.ScheduleWorkAsync(quote.Id, JobStart, JobStart.AddHours(1), theirs);

        Assert.Equal("La persona elegida no está activa en el equipo del negocio.", refused.Error);
    }

    [Fact]
    public async Task AJobAppointment_ShouldNeverBeChargedOnItsOwn_NorQuoted()
    {
        await using var shop = await Shop.OpenAsync(pricesIncludeTax: false, policy: HomeVisitChargePolicy.CreditedIfHired);
        var (_, quote) = await shop.CreditedVisitQuoteAsync();
        Assert.True((await shop.ConvertAsync(quote.Id)).IsSuccess);
        var work = Assert.Single((await shop.ScheduleWorkAsync(quote.Id, JobStart, JobStart.AddHours(3))).Value!.Order!.WorkAppointments);
        var service = await shop.Fixture.AddProductAsync(shop.Tenant, "Revisión", 300m, unit: UnitType.Service);

        var charged = await shop.Fixture.CreateOrderHandler().Handle(new CreateOrderCommand
        {
            OrderType = OrderType.Service,
            CustomerName = "Valeria Ruiz",
            CustomerPhone = "5522107765",
            Items = [new CreateOrderItemDto { ProductId = service.Id, Quantity = 1 }],
            AppointmentId = work.Id,
            Payments = [new CreateOrderPaymentDto { Amount = 348m, Method = OrderPaymentMethod.Cash }]
        }, CancellationToken.None);
        shop.Context.ChangeTracker.Clear();
        var quoted = await new CreateQuoteCommandHandler(
                shop.Context, shop.Fixture.TenantService, shop.Fixture.CurrentUser, new FolioGenerator(shop.Context), shop.Fixture.Clock)
            .Handle(new CreateQuoteCommand(work.Id, null, null, null, null), CancellationToken.None);

        Assert.Equal(AppointmentWorkRules.ChargedInItsOrderMessage, charged.Error);
        Assert.Equal(AppointmentWorkRules.NotQuotedMessage, quoted.Error);
        var appointment = await shop.Context.Appointments.AsNoTracking().SingleAsync(a => a.Id == work.Id);
        Assert.Equal((AppointmentStatus.Scheduled, (Guid?)null), (appointment.Status, appointment.SaleOrderId));
    }

    [Fact]
    public async Task TheCustomer_ShouldBeToldOfTheJob_WithoutAChargeForTheVisit()
    {
        await using var shop = await Shop.OpenAsync(pricesIncludeTax: false, policy: HomeVisitChargePolicy.CreditedIfHired);
        var (_, quote) = await shop.CreditedVisitQuoteAsync();
        Assert.True((await shop.ConvertAsync(quote.Id)).IsSuccess);
        await shop.ConnectWhatsAppAsync();
        var before = await shop.Context.OutgoingMessages.CountAsync(m => m.Purpose == MessagePurpose.AppointmentNotice);

        Assert.True((await shop.ScheduleWorkAsync(quote.Id, JobStart, JobStart.AddHours(3))).IsSuccess);

        var notice = await shop.Context.OutgoingMessages.AsNoTracking()
            .Where(m => m.Purpose == MessagePurpose.AppointmentNotice)
            .OrderByDescending(m => m.NextAttemptAt)
            .FirstAsync();
        Assert.Equal(before + 1, await shop.Context.OutgoingMessages.CountAsync(m => m.Purpose == MessagePurpose.AppointmentNotice));
        Assert.Contains($"Trabajo de la cotización {quote.Number}", notice.Body);
        Assert.Contains("a domicilio (CP 72000)", notice.Body);
        Assert.DoesNotContain("Cargo por visita", notice.Body);
        Assert.DoesNotContain("Visita:", notice.Body);
    }

    [Fact]
    public async Task TheOrder_TheAgenda_AndTheRoute_ShouldShowTheJob()
    {
        await using var shop = await Shop.OpenAsync(pricesIncludeTax: false, policy: HomeVisitChargePolicy.CreditedIfHired);
        var (_, quote) = await shop.CreditedVisitQuoteAsync();
        var order = (await shop.ConvertAsync(quote.Id)).Value!.Order!;
        var luis = await shop.StaffAsync("Luis");
        Assert.True((await shop.ScheduleWorkAsync(quote.Id, JobStart, JobStart.AddHours(3), luis)).IsSuccess);

        var detail = await new GetOrderByIdQueryHandler(shop.Context).Handle(new GetOrderByIdQuery(order.Id), CancellationToken.None);
        var agenda = await new GetAppointmentsQueryHandler(shop.Context)
            .Handle(new GetAppointmentsQuery { FromDate = JobStart.AddDays(-1), ToDate = JobStart.AddDays(1) }, CancellationToken.None);
        var route = await new GetVisitRouteQueryHandler(shop.Context, shop.Fixture.TenantService)
            .Handle(new GetVisitRouteQuery(DateOnly.FromDateTime(JobStart.AddHours(-6).Date)), CancellationToken.None);

        Assert.Equal((JobStart, "Luis Pérez"), (Assert.Single(detail.WorkAppointments).StartAt, detail.WorkAppointments[0].StaffName));
        var listed = Assert.Single(agenda.Items, a => a.WorkOrderId is not null);
        Assert.Equal((order.Id, order.OrderNumber), (listed.WorkOrderId, listed.WorkOrderNumber));
        var stop = Assert.Single(route.Staff.SelectMany(s => s.Stops), s => s.WorkOrderId is not null);
        Assert.Equal((order.OrderNumber, 0m), (stop.WorkOrderNumber, stop.Fee));
        // Every other appointment says no order.
        Assert.All(agenda.Items.Where(a => a.Id != listed.Id), a => Assert.Equal(((Guid?)null, (string?)null), (a.WorkOrderId, a.WorkOrderNumber)));
    }

    [Fact]
    public async Task AQuoteOfAnotherBusiness_ShouldNotHaveItsJobAgendado()
    {
        await using var shop = await Shop.OpenAsync();
        var quote = await shop.AcceptedAsync(new QuoteLineRequest { Description = "Revisión", Quantity = 1m, UnitPrice = 500m });
        Assert.True((await shop.ConvertAsync(quote.Id)).IsSuccess);
        var other = await shop.Fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments, slug: "otro");
        shop.Fixture.TenantService.SetTenant(other.Id);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => shop.ScheduleWorkAsync(quote.Id, JobStart, JobStart.AddHours(1)));
        Assert.Equal(0, await shop.Context.Appointments.IgnoreQueryFilters().CountAsync());
    }

    [Theory]
    [InlineData(0, "El trabajo termina después de empezar.")]
    [InlineData(-60, "El trabajo termina después de empezar.")]
    [InlineData(12 * 60 + 1, "Una cita del trabajo dura hasta 12 horas: agenda los demás días en otra.")]
    public void AJobAppointment_ShouldEndAfterItStarts_AndLastUpTo12Hours(int minutes, string message)
    {
        var result = new ScheduleQuoteWorkCommandValidator().Validate(
            new ScheduleQuoteWorkCommand(Guid.NewGuid(), JobStart, JobStart.AddMinutes(minutes), null, null));

        Assert.Contains(result.Errors, e => e.ErrorMessage == message);
        Assert.True(new ScheduleQuoteWorkCommandValidator().Validate(
            new ScheduleQuoteWorkCommand(Guid.NewGuid(), JobStart, JobStart.AddHours(12), null, null)).IsValid);
    }
}
