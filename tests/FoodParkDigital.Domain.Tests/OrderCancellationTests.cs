using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

public class OrderCancellationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Cancel_ShouldBeRejected_WhenTheOrderHasPayments()
    {
        var order = NewOrder(BusinessType.Retail, OrderType.Retail);
        order.RegisterPayment(50m, OrderPaymentMethod.Cash, Now, null, null, null, null);

        Assert.Throws<DomainException>(() => order.Cancel(new RetailOrderStateMachine(), "cajero"));
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public void RegisterPayment_ShouldBeRejected_WhenTheOrderIsCancelled()
    {
        var order = NewOrder(BusinessType.Retail, OrderType.Retail);
        order.Cancel(new RetailOrderStateMachine(), "cajero");

        Assert.Throws<DomainException>(() =>
            order.RegisterPayment(50m, OrderPaymentMethod.Cash, Now, null, null, null, null));
        Assert.Equal(0m, order.PaidAmount);
    }

    [Fact]
    public void Cancel_ShouldCancelOpenPieces_AndKeepCompletedOnes()
    {
        var order = NewOrder(BusinessType.Service, OrderType.Service);
        AddPiece(order, "Par 1");
        AddPiece(order, "Par 2");
        AddPiece(order, "Par 3");
        var pieces = order.ServicePieces.ToList();
        order.ChangeServicePieceStatus(pieces[1], ServiceOrderPieceStatus.InProgress, Now);
        order.ChangeServicePieceStatus(pieces[2], ServiceOrderPieceStatus.InProgress, Now);
        order.ChangeServicePieceStatus(pieces[2], ServiceOrderPieceStatus.Completed, Now);

        order.Cancel(new ServiceOrderStateMachine(), "admin", "Cliente no regreso");

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(ServiceOrderPieceStatus.Cancelled, pieces[0].Status);
        Assert.Equal(ServiceOrderPieceStatus.Cancelled, pieces[1].Status);
        Assert.Equal(ServiceOrderPieceStatus.Completed, pieces[2].Status);
    }

    [Fact]
    public void ChangeServicePieceStatus_ShouldTouchTheOrder_SoItsRowVersionIsChecked()
    {
        var order = NewOrder(BusinessType.Service, OrderType.Service);
        AddPiece(order, "Par 1");

        order.ChangeServicePieceStatus(order.ServicePieces.Single(), ServiceOrderPieceStatus.InProgress, Now);

        Assert.Equal(Now, order.UpdatedAt);
    }

    [Fact]
    public void ChangeServicePieceStatus_ShouldRejectPiecesOfAnotherOrder()
    {
        var order = NewOrder(BusinessType.Service, OrderType.Service);
        var other = NewOrder(BusinessType.Service, OrderType.Service);
        AddPiece(other, "Ajena");

        Assert.Throws<DomainException>(() =>
            order.ChangeServicePieceStatus(other.ServicePieces.Single(), ServiceOrderPieceStatus.InProgress, Now));
    }

    [Theory]
    [InlineData(BusinessType.Food, OrderType.Retail)]
    [InlineData(BusinessType.Retail, OrderType.Retail)]
    [InlineData(BusinessType.Service, OrderType.Service)]
    public void CanCancel_ShouldMatchTheTransitionTable(BusinessType businessType, OrderType orderType)
    {
        var stateMachine = OrderStateMachineFactory.Create(businessType, orderType);

        foreach (var status in Enum.GetValues<OrderStatus>())
        {
            var allowsCancelledTransition = stateMachine.GetAllowedTransitions(status).Contains(OrderStatus.Cancelled);
            Assert.Equal(allowsCancelledTransition, stateMachine.CanCancel(status));
        }
    }

    private static Order NewOrder(BusinessType businessType, OrderType orderType)
    {
        var order = new Order(
            tenantId: Guid.NewGuid(),
            customerUserId: Guid.NewGuid(),
            temporaryCustomerId: null,
            createdByBusiness: true,
            orderNumber: "ORD-000001",
            businessType: businessType,
            orderType: orderType,
            customerName: "Cliente",
            customerPhone: "+525511112222",
            customerNotes: null,
            tableNumber: null);
        order.AddItem(Guid.NewGuid(), "Producto", 1, 100m);
        return order;
    }

    private static void AddPiece(Order order, string name) =>
        order.AddServicePiece(name, null, null, null, null, null, null, null, null);
}
