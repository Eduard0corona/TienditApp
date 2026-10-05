using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Purchasing.Entities;
using FoodParkDigital.Domain.Purchasing.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>A purchase that arrived in part can close without waiting for the rest, keeping what was asked for.</summary>
public class PurchaseOrderClosingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static (PurchaseOrder Order, PurchaseOrderLine Line) PartiallyReceived()
    {
        var order = new PurchaseOrder(Guid.NewGuid(), Guid.NewGuid(), "PO-1", Now.AddDays(-2));
        var line = order.AddLine(Guid.NewGuid(), Guid.NewGuid(), quantity: 10m, unitCost: 12m, taxPercent: 0m);
        order.MarkAsSent();
        order.RegisterReception(new Dictionary<Guid, decimal> { [line.Id] = 4m }, Now.AddDays(-1));
        return (order, line);
    }

    [Fact]
    public void Close_ShouldKeepWhatWasOrderedAndWhyTheRestIsNotExpected()
    {
        var (order, line) = PartiallyReceived();

        order.CloseWithoutRemainder("  El proveedor ya no tiene  ", Now);

        Assert.Equal(PurchaseOrderStatus.Closed, order.Status);
        Assert.Equal(Now, order.ClosedAt);
        Assert.Equal("El proveedor ya no tiene", order.CloseReason);
        // Nothing is rewritten: 10 were ordered and 4 came in.
        Assert.Equal(10m, line.Quantity);
        Assert.Equal(4m, line.ReceivedQuantity);
    }

    [Fact]
    public void ClosedOrder_ShouldNotReceiveCancelOrGrow()
    {
        var (order, line) = PartiallyReceived();
        order.CloseWithoutRemainder(null, Now);

        var receive = Assert.Throws<DomainException>(() =>
            order.RegisterReception(new Dictionary<Guid, decimal> { [line.Id] = 1m }, Now));
        Assert.Throws<DomainException>(order.Cancel);
        Assert.Throws<DomainException>(() => order.AddLine(Guid.NewGuid(), Guid.NewGuid(), 1m, 1m, 0m));

        Assert.StartsWith("La compra ya se cerró", receive.Message);
    }

    [Fact]
    public void OnlyAPartiallyReceivedOrder_ShouldClose()
    {
        var sent = new PurchaseOrder(Guid.NewGuid(), Guid.NewGuid(), "PO-2", Now);
        sent.AddLine(Guid.NewGuid(), Guid.NewGuid(), 1m, 5m, 0m);
        sent.MarkAsSent();

        var error = Assert.Throws<DomainException>(() => sent.CloseWithoutRemainder(null, Now));

        Assert.Contains("cancélala", error.Message);
        Assert.Throws<DomainException>(() => PartiallyReceived().Order.CloseWithoutRemainder(new string('x', 501), Now));
    }
}
