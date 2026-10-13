using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// An appointment at the customer's address (Fase 22E): the address as written, the zone that reaches it and the charge
/// agreed then, kept while the address stays the same; the appointment done with its sale; and the visit charge in the
/// sale as one more concept, like the delivery of an order.
/// </summary>
public class HomeVisitTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 10, 14, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 17, 0, 0, TimeSpan.Zero);
    private static readonly DeliveryAddress Address = DeliveryAddress.ForVisit("72000", "Av. Juárez 123, Col. Centro", "Portón negro");

    [Theory]
    [InlineData(null, "Av. Juárez 123", DeliveryAddress.VisitPostalCodeRequiredMessage)]
    [InlineData("7200", "Av. Juárez 123", "«7200» no es un código postal: lleva 5 dígitos.")]
    [InlineData("72000", "  ", DeliveryAddress.VisitAddressLineRequiredMessage)]
    public void VisitAddress_ShouldSayWhatIsMissing_ForAVisit(string? postalCode, string addressLine, string message)
        => Assert.Equal(message, Assert.Throws<DomainException>(() => DeliveryAddress.ForVisit(postalCode, addressLine, null)).Message);

    [Fact]
    public void VisitAddress_ShouldFollowTheRulesOfAnAddress()
    {
        var address = DeliveryAddress.ForVisit(" 72000 ", "  Av. Juárez 123 ", "   ");

        Assert.Equal(("72000", "Av. Juárez 123", (string?)null), (address.PostalCode, address.AddressLine, address.References));
        Assert.Equal(DeliveryAddress.AddressLineTooLongMessage,
            Assert.Throws<DomainException>(() => DeliveryAddress.ForVisit("72000", new string('a', 301), null)).Message);
        Assert.Equal(DeliveryAddress.ReferencesTooLongMessage,
            Assert.Throws<DomainException>(() => DeliveryAddress.ForVisit("72000", "Av. Juárez 123", new string('a', 501))).Message);
    }

    [Fact]
    public void ArrangeVisit_ShouldKeepTheAddress_TheZoneAndItsFee()
    {
        var appointment = NewAppointment();

        Assert.True(appointment.ArrangeVisit(Address, Zone(fee: 80m), Now));

        var visit = appointment.Visit!;
        Assert.True(appointment.IsHomeVisit);
        // The appointment changes with its visit, so its version catches a charge saved meanwhile.
        Assert.Equal(Now, appointment.UpdatedAt);
        Assert.Equal((Tenant, appointment.Id), (visit.TenantId, visit.AppointmentId));
        Assert.Equal(("72000", "Av. Juárez 123, Col. Centro", "Portón negro", "Centro", 80m),
            (visit.PostalCode, visit.AddressLine, visit.References, visit.ZoneName, visit.Fee));
    }

    [Fact]
    public void ArrangeVisit_ShouldKeepWhatWasAgreed_AtTheSameAddress_EvenIfTheZoneChanged()
    {
        var appointment = NewAppointment();
        appointment.ArrangeVisit(Address, Zone(fee: 80m), Now);

        Assert.False(appointment.ArrangeVisit(DeliveryAddress.ForVisit("72000", "Av. Juárez 123, Col. Centro", "Portón negro"),
            Zone(name: "Centro histórico", fee: 120m), Now));

        Assert.Equal(("Centro", 80m), (appointment.Visit!.ZoneName, appointment.Visit.Fee));
    }

    [Fact]
    public void ArrangeVisit_ShouldQuoteAgain_AtAnotherAddress()
    {
        var appointment = NewAppointment();
        appointment.ArrangeVisit(Address, Zone(fee: 80m), Now);
        var visitId = appointment.Visit!.Id;

        Assert.True(appointment.ArrangeVisit(DeliveryAddress.ForVisit("72005", "Calle 5 Sur 300", null), Zone(name: "Sur", fee: 0m), Now));

        // The same visit, moved: the agenda keeps one per appointment.
        Assert.Equal((visitId, "72005", "Calle 5 Sur 300", (string?)null, "Sur", 0m),
            (appointment.Visit!.Id, appointment.Visit.PostalCode, appointment.Visit.AddressLine, appointment.Visit.References,
                appointment.Visit.ZoneName, appointment.Visit.Fee));
    }

    [Fact]
    public void ArrangeVisit_ShouldNeedAZoneOfTheBusiness_ThatReachesThePostalCodeNow()
    {
        var expected = HomeVisitRules.NotCoveredMessage("72000");

        Assert.Equal(expected, RefusedVisit(Zone(postalCodes: ["72100"])));
        Assert.Equal(expected, RefusedVisit(Zone(isActive: false)));
        Assert.Equal(expected, RefusedVisit(Zone(tenantId: Guid.NewGuid())));
    }

    [Theory]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    public void ClosedAppointment_ShouldNotChangeWhereItIs(AppointmentStatus status)
    {
        var appointment = NewAppointment();
        appointment.ArrangeVisit(Address, Zone(), Now);
        appointment.ChangeStatus(status);

        Assert.Throws<DomainException>(() => appointment.ArrangeVisit(DeliveryAddress.ForVisit("72005", "Calle 5 Sur 300", null), Zone(), Now));
        Assert.Throws<DomainException>(() => appointment.MoveToBusiness(Now));
        Assert.Equal("72000", appointment.Visit!.PostalCode);
    }

    [Fact]
    public void MoveToBusiness_ShouldDropTheVisit_AndSayWhenItWasNotOne()
    {
        var appointment = NewAppointment();
        Assert.False(appointment.MoveToBusiness(Now));

        appointment.ArrangeVisit(Address, Zone(), Now);

        appointment.UpdatedAt = null;

        Assert.True(appointment.MoveToBusiness(Now));
        Assert.Null(appointment.Visit);
        Assert.Equal(Now, appointment.UpdatedAt);
        Assert.False(appointment.IsHomeVisit);
    }

    [Theory]
    [InlineData(AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.InService)]
    public void RecordSale_ShouldCompleteAnOpenAppointment(AppointmentStatus status)
    {
        var appointment = NewAppointment();
        if (status != AppointmentStatus.Scheduled)
            appointment.ChangeStatus(status);
        var order = Guid.NewGuid();

        appointment.RecordSale(order);

        Assert.Equal((order, AppointmentStatus.Completed), (appointment.SaleOrderId, appointment.Status));
    }

    [Fact]
    public void RecordSale_ShouldTakeAnAppointmentAlreadyDone_ThatHadNoSale()
    {
        var appointment = NewAppointment();
        appointment.ChangeStatus(AppointmentStatus.Completed);
        var order = Guid.NewGuid();

        appointment.RecordSale(order);

        Assert.Equal((order, AppointmentStatus.Completed), (appointment.SaleOrderId, appointment.Status));
    }

    [Fact]
    public void RecordSale_ShouldRefuseACancelledAppointment_OrOneTheCustomerMissed()
    {
        var cancelled = NewAppointment();
        cancelled.ChangeStatus(AppointmentStatus.Cancelled);
        var missed = NewAppointment();
        missed.ChangeStatus(AppointmentStatus.NoShow);

        Assert.Equal(HomeVisitRules.CancelledSaleMessage, Assert.Throws<DomainException>(() => cancelled.RecordSale(Guid.NewGuid())).Message);
        Assert.Equal(HomeVisitRules.NoShowSaleMessage, Assert.Throws<DomainException>(() => missed.RecordSale(Guid.NewGuid())).Message);
        Assert.Null(cancelled.SaleOrderId);
        Assert.Null(missed.SaleOrderId);
    }

    [Fact]
    public void RecordSale_ShouldBeOnce_UnlessTheSaleWasCancelled()
    {
        var appointment = NewAppointment();
        var first = Guid.NewGuid();
        appointment.RecordSale(first);

        Assert.Equal(HomeVisitRules.AlreadySoldMessage,
            Assert.Throws<DomainException>(() => appointment.RecordSale(Guid.NewGuid())).Message);
        Assert.Equal(first, appointment.SaleOrderId);

        var second = Guid.NewGuid();
        appointment.RecordSale(second, previousSaleCancelled: true);
        Assert.Equal((second, AppointmentStatus.Completed), (appointment.SaleOrderId, appointment.Status));
    }

    [Fact]
    public void RecordSale_ShouldNeedTheSale()
        => Assert.Throws<ArgumentException>(() => NewAppointment().RecordSale(Guid.Empty));

    [Fact]
    public void VisitCharge_ShouldEnterTheTotal_AsOneMoreConceptWithIvaIncluded()
    {
        var order = SaleWith(400m);
        var appointment = Guid.NewGuid();

        var charge = order.ChargeVisit(appointment, " Centro ", 80m, VatRate.General);

        Assert.Equal((Tenant, order.Id, appointment, "Centro", 80m, 16m),
            (charge.TenantId, charge.OrderId, charge.AppointmentId, charge.ZoneName, charge.Fee, charge.FeeTaxRate));
        Assert.Same(charge, order.VisitCharge);
        Assert.Equal(400m, order.ProductsTotal);
        Assert.Equal(480m, order.Total);
        var tax = Assert.Single(order.GetTaxBreakdown());
        Assert.Equal((413.79m, 66.21m), (tax.TaxableBase, tax.TaxAmount));
    }

    [Fact]
    public void VisitCharge_ShouldFollowTheTaxPolicy_OfASaleWithPricesBeforeIva()
    {
        var order = SaleWith(400m, pricesIncludeTax: false);

        order.ChargeVisit(Guid.NewGuid(), "Centro", 80m, VatRate.General);

        // Like any price of a business whose prices do not include IVA: 80 plus 16 %.
        Assert.Equal((480m, 76.80m, 556.80m), (order.SubTotal, order.Tax, order.Total));
    }

    [Fact]
    public void VisitCharge_ShouldKeepItsOwnRate_WhenTheServicesHaveAnother()
    {
        var order = SaleWith(400m, VatRate.Of(0m));

        order.ChargeVisit(Guid.NewGuid(), "Centro", 80m, VatRate.Of(8m));

        Assert.Equal([(8m, 74.07m, 5.93m), (0m, 400m, 0m)],
            order.GetTaxBreakdown().Select(line => (line.RatePercent, line.TaxableBase, line.TaxAmount)));
        Assert.Equal(480m, order.Total);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_000.01)]
    [InlineData(80.005)]
    public void VisitCharge_ShouldBeBetweenOneCentAndTheLimit(decimal fee)
        => Assert.Equal(HomeVisitRules.FeeMessage, Refused(order => order.ChargeVisit(Guid.NewGuid(), "Centro", fee, VatRate.General)));

    [Fact]
    public void VisitCharge_ShouldBeAddedOnce_ToANewSaleWithItsServices_BeforeAnyPayment()
    {
        Assert.Equal(HomeVisitRules.AlreadyChargedMessage, Refused(order =>
        {
            order.ChargeVisit(Guid.NewGuid(), "Centro", 80m, VatRate.General);
            order.ChargeVisit(Guid.NewGuid(), "Centro", 80m, VatRate.General);
        }));
        Assert.Equal(HomeVisitRules.NeedsItemsMessage,
            Assert.Throws<DomainException>(() => NewSale().ChargeVisit(Guid.NewGuid(), "Centro", 80m, VatRate.General)).Message);
        Assert.Equal(HomeVisitRules.BeforeAnyPaymentMessage, Refused(order =>
        {
            order.RegisterPayment(50m, OrderPaymentMethod.Cash, Now, null, null, null, null);
            order.ChargeVisit(Guid.NewGuid(), "Centro", 80m, VatRate.General);
        }));
        Assert.Equal(HomeVisitRules.OnlyNewOrdersMessage, Refused(order =>
        {
            order.TransitionTo(OrderStatus.Confirmed, new ServiceOrderStateMachine(), "caja");
            order.ChargeVisit(Guid.NewGuid(), "Centro", 80m, VatRate.General);
        }));
    }

    [Fact]
    public void VisitCharge_ShouldNotGoOnATab_NorOnAnOrderThatGoesToTheAddress()
    {
        var tab = SaleWith(100m, orderType: OrderType.Retail);
        tab.OpenAsTab(null, "Mesa 4", Now, "mesero");
        Assert.Equal(HomeVisitRules.NotForTabsMessage,
            Assert.Throws<DomainException>(() => tab.ChargeVisit(Guid.NewGuid(), "Centro", 80m, VatRate.General)).Message);

        var delivery = SaleWith(100m, orderType: OrderType.Retail);
        delivery.ArrangeDelivery(DeliveryAddress.From("72000", "Av. Juárez 123", null), Zone(), VatRate.General);
        Assert.Equal(HomeVisitRules.NotForDeliveriesMessage,
            Assert.Throws<DomainException>(() => delivery.ChargeVisit(Guid.NewGuid(), "Centro", 80m, VatRate.General)).Message);
    }

    [Fact]
    public void SaleWithItsVisitCharge_ShouldNotBecomeATab_NorGoToTheAddress()
    {
        var order = SaleWith(100m, orderType: OrderType.Retail);
        order.ChargeVisit(Guid.NewGuid(), "Centro", 80m, VatRate.General);

        Assert.Equal(HomeVisitRules.NotForTabsMessage,
            Assert.Throws<DomainException>(() => order.OpenAsTab(null, "Mesa 4", Now, "mesero")).Message);
        Assert.Equal(HomeVisitRules.NotForDeliveriesMessage, Assert.Throws<DomainException>(() =>
            order.ArrangeDelivery(DeliveryAddress.From("72000", "Av. Juárez 123", null), Zone(), VatRate.General)).Message);
    }

    [Fact]
    public void PlatformOrder_ShouldNotTakeAVisitCharge()
    {
        var order = new Order(
            Tenant, null, null, createdByBusiness: true, Order.NumberNotAssigned, BusinessType.Food, OrderType.Retail,
            "Juan", customerPhone: null, customerNotes: null, tableNumber: null, pricesIncludeTax: true, channel: OrderChannel.Rappi);
        order.AddItem(Guid.NewGuid(), "Tacos", 1, 100m);

        Assert.Equal(HomeVisitRules.NotForPlatformsMessage,
            Assert.Throws<DomainException>(() => order.ChargeVisit(Guid.NewGuid(), "Centro", 80m, VatRate.General)).Message);
    }

    [Fact]
    public void VisitCharge_ShouldBePaidWithTheSale()
    {
        var order = SaleWith(400m);
        order.ChargeVisit(Guid.NewGuid(), "Centro", 80m, VatRate.General);

        order.RegisterPayment(480m, OrderPaymentMethod.Cash, Now, null, null, null, null);

        Assert.Equal((480m, 0m), (order.PaidAmount, order.Total - order.PaidAmount));
    }

    [Fact]
    public void TravelMinutes_ShouldStartAtThirty_AndGoFromZeroToFourHours()
    {
        var settings = new TenantSettings(Tenant, TenantCapability.Service | TenantCapability.Appointments);
        Assert.Equal(HomeVisitRules.DefaultTravelMinutes, settings.HomeVisitTravelMinutes);

        settings.SetHomeVisitTravelMinutes(0);
        Assert.Equal(0, settings.HomeVisitTravelMinutes);
        settings.SetHomeVisitTravelMinutes(240);
        Assert.Equal(240, settings.HomeVisitTravelMinutes);

        Assert.Equal(HomeVisitRules.TravelMinutesMessage,
            Assert.Throws<DomainException>(() => settings.SetHomeVisitTravelMinutes(-1)).Message);
        Assert.Equal(HomeVisitRules.TravelMinutesMessage,
            Assert.Throws<DomainException>(() => settings.SetHomeVisitTravelMinutes(241)).Message);
        Assert.Equal(240, settings.HomeVisitTravelMinutes);
    }

    [Fact]
    public void Messages_ShouldSayThePostalCode_AndWhoNeedsTheTravelTime()
    {
        Assert.Equal("Por ahora no hacemos visitas en el 72000.", HomeVisitRules.NotCoveredMessage("72000"));
        Assert.Equal("Ana López necesita 30 minutos de traslado entre una visita a domicilio y su otra cita.",
            HomeVisitRules.TravelMessage("Ana López", 30));
    }

    private static string RefusedVisit(DeliveryZone zone)
    {
        var appointment = NewAppointment();
        var message = Assert.Throws<DomainException>(() => appointment.ArrangeVisit(Address, zone, Now)).Message;
        Assert.Null(appointment.Visit);
        return message;
    }

    private static string Refused(Action<Order> act)
    {
        var order = SaleWith(400m);
        return Assert.Throws<DomainException>(() => act(order)).Message;
    }

    private static Appointment NewAppointment()
        => new(Tenant, "Ana", "5512345678", "Corte a domicilio", Start, Start.AddHours(1), "Luis", serviceProductId: Guid.NewGuid());

    private static Order NewSale(bool pricesIncludeTax = true, OrderType orderType = OrderType.Service)
        => new(Tenant, null, null, createdByBusiness: true, Order.NumberNotAssigned, BusinessType.Service, orderType,
            "Ana", customerPhone: "5512345678", customerNotes: null, tableNumber: null, pricesIncludeTax: pricesIncludeTax,
            channel: OrderChannel.Counter);

    private static Order SaleWith(
        decimal amount, VatRate? rate = null, bool pricesIncludeTax = true, OrderType orderType = OrderType.Service)
    {
        var order = NewSale(pricesIncludeTax, orderType);
        order.AddItem(Guid.NewGuid(), "Corte a domicilio", 1, amount, vatRate: rate ?? VatRate.General);
        return order;
    }

    private static DeliveryZone Zone(
        string name = "Centro", decimal fee = 35m, string[]? postalCodes = null, bool isActive = true, Guid? tenantId = null)
        => new(tenantId ?? Tenant, new DeliveryZoneFields(name, fee, 0m, null, postalCodes ?? ["72000", "72005"], isActive));
}
