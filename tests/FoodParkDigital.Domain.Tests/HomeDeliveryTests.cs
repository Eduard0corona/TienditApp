using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// An order that goes to the customer's address (Fase 22C): the address as written, the zone that reaches it, its minimum
/// and fee in the total as one more concept, an order fixed once its delivery is worked out, never closed by being paid
/// and ready, and the courier's trip written down and corrected.
/// </summary>
public class HomeDeliveryTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 19, 0, 0, TimeSpan.Zero);
    private static readonly DeliveryAddress Address = DeliveryAddress.From("72000", "Av. Juárez 123, Col. Centro", "Portón negro");

    [Fact]
    public void Address_ShouldBeTrimmed_WithReferencesOptional()
    {
        var address = DeliveryAddress.From(" 72000 ", "  Av. Juárez 123, Col. Centro ", "   ");

        Assert.Equal(("72000", "Av. Juárez 123, Col. Centro", (string?)null), (address.PostalCode, address.AddressLine, address.References));
        Assert.Equal("Portón negro", Address.References);
    }

    [Theory]
    [InlineData(null, "Av. Juárez 123", null, DeliveryAddress.PostalCodeRequiredMessage)]
    [InlineData("7200", "Av. Juárez 123", null, "«7200» no es un código postal: lleva 5 dígitos.")]
    [InlineData("72000", "  ", null, DeliveryAddress.AddressLineRequiredMessage)]
    public void Address_ShouldSayWhatIsMissingOrWrong(string? postalCode, string addressLine, string? references, string message)
        => Assert.Equal(message, Assert.Throws<DomainException>(() => DeliveryAddress.From(postalCode, addressLine, references)).Message);

    [Fact]
    public void Address_ShouldRefuseTextsTooLong()
    {
        Assert.Equal(DeliveryAddress.AddressLineTooLongMessage,
            Assert.Throws<DomainException>(() => DeliveryAddress.From("72000", new string('a', 301), null)).Message);
        Assert.Equal(DeliveryAddress.ReferencesTooLongMessage,
            Assert.Throws<DomainException>(() => DeliveryAddress.From("72000", "Av. Juárez 123", new string('a', 501))).Message);
    }

    [Fact]
    public void Shipment_ShouldNormalizeTheFolio_AndKeepAnHttpsLinkOnly()
    {
        var shipment = CourierShipment.From(DeliveryCourier.Paquetenvia, " #pq 1234-ab ", " https://paquetenvia.mx/rastreo/PQ1234-AB ");

        Assert.Equal("PQ1234-AB", shipment.Folio);
        Assert.Equal("https://paquetenvia.mx/rastreo/PQ1234-AB", shipment.TrackingUrl);
        Assert.Null(CourierShipment.From(DeliveryCourier.Paquetenvia, "PQ1", "  ").TrackingUrl);
        // The scheme in capitals is kept in lower case, so the check of the database reads it whatever its collation.
        Assert.Equal("https://paquetenvia.mx/r/PQ1", CourierShipment.From(DeliveryCourier.Paquetenvia, "PQ1", " HTTPS://paquetenvia.mx/r/PQ1 ").TrackingUrl);
    }

    [Theory]
    [InlineData("", null, CourierShipment.FolioRequiredMessage)]
    [InlineData("PQ 12/3", null, CourierShipment.FolioInvalidMessage)]
    [InlineData("PQ1", "http://paquetenvia.mx/rastreo", CourierShipment.TrackingUrlMessage)]
    [InlineData("PQ1", "javascript:alert(1)", CourierShipment.TrackingUrlMessage)]
    [InlineData("PQ1", "https://usuario:clave@paquetenvia.mx", CourierShipment.TrackingUrlMessage)]
    [InlineData("PQ1", "paquetenvia.mx/rastreo", CourierShipment.TrackingUrlMessage)]
    // .NET reads «https:\\host» as https, but the database (and a browser) would not: it must say «https://».
    [InlineData("PQ1", "https:\\\\paquetenvia.mx\\rastreo", CourierShipment.TrackingUrlMessage)]
    [InlineData("PQ1", "https:/paquetenvia.mx/rastreo", CourierShipment.TrackingUrlMessage)]
    public void Shipment_ShouldSayWhatIsWrong(string folio, string? trackingUrl, string message)
        => Assert.Equal(message,
            Assert.Throws<DomainException>(() => CourierShipment.From(DeliveryCourier.Paquetenvia, folio, trackingUrl)).Message);

    [Fact]
    public void Shipment_ShouldRefuseAnUnknownCourier_AndALinkTooLong()
    {
        Assert.Equal(CourierShipment.CourierMessage,
            Assert.Throws<DomainException>(() => CourierShipment.From((DeliveryCourier)9, "PQ1", null)).Message);
        Assert.Equal(CourierShipment.TrackingUrlMessage,
            Assert.Throws<DomainException>(() => CourierShipment.From(
                DeliveryCourier.Paquetenvia, "PQ1", "https://paquetenvia.mx/" + new string('a', 480))).Message);
    }

    [Fact]
    public void Delivery_ShouldEnterTheTotal_AsOneMoreConceptWithIvaIncluded()
    {
        var order = OrderWith(100m);

        var delivery = order.ArrangeDelivery(Address, Zone(fee: 35m), VatRate.General);

        Assert.Equal(("72000", "Centro", 35m, 16m), (delivery.PostalCode, delivery.ZoneName, delivery.Fee, delivery.FeeTaxRate));
        Assert.Equal(100m, order.ProductsTotal);
        Assert.Equal(135m, order.Total);
        var tax = Assert.Single(order.GetTaxBreakdown());
        Assert.Equal((116.38m, 18.62m), (tax.TaxableBase, tax.TaxAmount));
        Assert.Equal((116.38m, 18.62m), (order.SubTotal, order.Tax));
    }

    [Fact]
    public void Delivery_ShouldFollowTheTaxPolicy_OfAnOrderWithPricesBeforeIva()
    {
        var order = OrderWith(100m, pricesIncludeTax: false);

        order.ArrangeDelivery(Address, Zone(fee: 35m), VatRate.General);

        // Like any price of a business whose prices do not include IVA: 35 plus 16 %.
        Assert.Equal(116m, order.ProductsTotal);
        Assert.Equal((135m, 21.60m, 156.60m), (order.SubTotal, order.Tax, order.Total));
    }

    [Fact]
    public void Delivery_ShouldKeepItsOwnRate_WhenTheProductsHaveAnother()
    {
        var order = OrderWith(100m, VatRate.Of(0m));

        order.ArrangeDelivery(Address, Zone(fee: 35m), VatRate.Of(8m));

        Assert.Equal([(8m, 32.41m, 2.59m), (0m, 100m, 0m)],
            order.GetTaxBreakdown().Select(line => (line.RatePercent, line.TaxableBase, line.TaxAmount)));
        Assert.Equal(135m, order.Total);
    }

    [Fact]
    public void Delivery_ShouldBeFree_FromTheZonesAmount_AndRefuseAnOrderBelowItsMinimum()
    {
        var free = OrderWith(400m);
        free.ArrangeDelivery(Address, Zone(fee: 35m, minimum: 150m, freeFrom: 400m), VatRate.General);
        Assert.Equal((0m, 400m), (free.Delivery!.Fee, free.Total));
        Assert.Single(free.GetTaxBreakdown());

        var small = OrderWith(149.99m);
        var error = Assert.Throws<DomainException>(
            () => small.ArrangeDelivery(Address, Zone(fee: 35m, minimum: 150m), VatRate.General));
        Assert.Equal("El pedido mínimo para el 72000 es de $150.00.", error.Message);
        Assert.Null(small.Delivery);
        Assert.Equal(149.99m, small.Total);
    }

    [Fact]
    public void Delivery_ShouldNeedAZoneOfTheBusiness_ThatReachesThePostalCodeNow()
    {
        var expected = HomeDeliveryRules.NotCoveredMessage("72000");

        Assert.Equal(expected, Refused(order => order.ArrangeDelivery(Address, Zone(postalCodes: ["72100"]), VatRate.General)));
        Assert.Equal(expected, Refused(order => order.ArrangeDelivery(Address, Zone(isActive: false), VatRate.General)));
        Assert.Equal(expected, Refused(order => order.ArrangeDelivery(Address, Zone(tenantId: Guid.NewGuid()), VatRate.General)));
    }

    [Fact]
    public void Delivery_ShouldBeWorkedOutOnce_OnANewOrderOfProducts_BeforeAnyPayment()
    {
        Assert.Equal(HomeDeliveryRules.AlreadyArrangedMessage, Refused(order =>
        {
            order.ArrangeDelivery(Address, Zone(), VatRate.General);
            order.ArrangeDelivery(Address, Zone(), VatRate.General);
        }));
        Assert.Equal(HomeDeliveryRules.NeedsProductsMessage,
            Refused(_ => NewOrder().ArrangeDelivery(Address, Zone(), VatRate.General)));
        Assert.Equal(HomeDeliveryRules.OnlyProductsMessage,
            Refused(_ => OrderWith(100m, orderType: OrderType.Service).ArrangeDelivery(Address, Zone(), VatRate.General)));
        Assert.Equal(HomeDeliveryRules.BeforeAnyPaymentMessage, Refused(order =>
        {
            order.RegisterPayment(50m, OrderPaymentMethod.Cash, Now, null, null, null, null);
            order.ArrangeDelivery(Address, Zone(), VatRate.General);
        }));
        Assert.Equal(HomeDeliveryRules.OnlyNewOrdersMessage, Refused(order =>
        {
            order.TransitionTo(OrderStatus.Confirmed, new FoodOrderStateMachine(), "caja");
            order.ArrangeDelivery(Address, Zone(), VatRate.General);
        }));
        Assert.Equal(HomeDeliveryRules.NotForTabsMessage, Refused(order =>
        {
            order.OpenAsTab(null, "Mesa 4", Now, "mesero");
            order.ArrangeDelivery(Address, Zone(), VatRate.General);
        }));
    }

    [Fact]
    public void PlatformOrder_ShouldNotGoToTheAddress_TheAppDeliversIt()
    {
        var order = new Order(
            Tenant, null, null, createdByBusiness: true, Order.NumberNotAssigned, BusinessType.Food, OrderType.Retail,
            "Juan", customerPhone: null, customerNotes: null, tableNumber: null, pricesIncludeTax: true, channel: OrderChannel.Rappi);
        order.AddItem(Guid.NewGuid(), "Tacos", 1, 100m);

        Assert.Equal(HomeDeliveryRules.NotForPlatformsMessage,
            Assert.Throws<DomainException>(() => order.ArrangeDelivery(Address, Zone(), VatRate.General)).Message);
    }

    [Fact]
    public void OrderWithItsDelivery_ShouldTakeNoMoreProducts_NorBecomeATab()
    {
        var order = OrderWith(100m);
        order.ArrangeDelivery(Address, Zone(), VatRate.General);

        Assert.Equal(HomeDeliveryRules.NoItemsAfterArrangedMessage,
            Assert.Throws<DomainException>(() => order.AddItem(Guid.NewGuid(), "Agua", 1, 20m)).Message);
        Assert.Equal(HomeDeliveryRules.NotForTabsMessage,
            Assert.Throws<DomainException>(() => order.OpenAsTab(null, "Mesa 4", Now, "mesero")).Message);
    }

    [Fact]
    public void PaidDeliveryReadyInTheKitchen_ShouldNotCloseItself_SomeoneMarksItDelivered()
    {
        var stateMachine = new FoodOrderStateMachine();
        var order = OrderWith(100m);
        order.ArrangeDelivery(Address, Zone(fee: 35m), VatRate.General);
        order.RegisterPayment(order.Total, OrderPaymentMethod.Cash, Now, null, null, null, null);
        order.TransitionTo(OrderStatus.Confirmed, stateMachine, "caja");
        order.TransitionTo(OrderStatus.Ready, stateMachine, "cocina");

        Assert.False(order.CloseIfPaidCounterSale(stateMachine, waitsForKitchen: true, "caja"));
        Assert.Equal(OrderStatus.Ready, order.Status);
    }

    [Fact]
    public void Shipment_ShouldBeWrittenDown_AndCorrected_ButNotOnACancelledOrder()
    {
        var order = OrderWith(100m);
        order.ArrangeDelivery(Address, Zone(), VatRate.General);

        Assert.True(order.RegisterCourierShipment(CourierShipment.From(DeliveryCourier.Paquetenvia, "PQ1", null), Now, "caja"));
        Assert.False(order.RegisterCourierShipment(CourierShipment.From(DeliveryCourier.Paquetenvia, "pq1", null), Now.AddMinutes(1), "otro"));
        Assert.Equal(("caja", Now), (order.Delivery!.ShipmentRegisteredBy, order.Delivery.ShipmentRegisteredAt));

        var link = "https://paquetenvia.mx/rastreo/PQ2";
        Assert.True(order.RegisterCourierShipment(CourierShipment.From(DeliveryCourier.Paquetenvia, "PQ2", link), Now.AddMinutes(5), "gerente"));
        Assert.Equal((DeliveryCourier.Paquetenvia, "PQ2", link, "gerente"),
            (order.Delivery.Courier, order.Delivery.CourierFolio, order.Delivery.TrackingUrl, order.Delivery.ShipmentRegisteredBy));

        order.Cancel(new FoodOrderStateMachine(), "gerente", "El cliente canceló");
        Assert.Equal(HomeDeliveryRules.CancelledMessage, Assert.Throws<DomainException>(() =>
            order.RegisterCourierShipment(CourierShipment.From(DeliveryCourier.Paquetenvia, "PQ3", null), Now, "caja")).Message);
    }

    [Fact]
    public void Shipment_ShouldNeedAnOrderThatGoesToTheAddress()
        => Assert.Equal(HomeDeliveryRules.NotADeliveryMessage, Assert.Throws<DomainException>(() =>
            OrderWith(100m).RegisterCourierShipment(CourierShipment.From(DeliveryCourier.Paquetenvia, "PQ1", null), Now, "caja")).Message);

    private static string Refused(Action<Order> act)
    {
        var order = OrderWith(100m);
        return Assert.Throws<DomainException>(() => act(order)).Message;
    }

    private static Order NewOrder(bool pricesIncludeTax = true, OrderType orderType = OrderType.Retail)
        => new(Tenant, null, Guid.NewGuid(), createdByBusiness: true, Order.NumberNotAssigned, BusinessType.Food, orderType,
            "Ana", customerPhone: "5512345678", customerNotes: null, tableNumber: null, pricesIncludeTax: pricesIncludeTax,
            channel: OrderChannel.WhatsApp);

    private static Order OrderWith(
        decimal amount, VatRate? rate = null, bool pricesIncludeTax = true, OrderType orderType = OrderType.Retail)
    {
        var order = NewOrder(pricesIncludeTax, orderType);
        order.AddItem(Guid.NewGuid(), "Comida corrida", 1, amount, vatRate: rate ?? VatRate.General);
        return order;
    }

    private static DeliveryZone Zone(
        decimal fee = 35m, decimal minimum = 0m, decimal? freeFrom = null, string[]? postalCodes = null, bool isActive = true,
        Guid? tenantId = null)
        => new(tenantId ?? Tenant, new DeliveryZoneFields("Centro", fee, minimum, freeFrom, postalCodes ?? ["72000", "72005"], isActive));
}
