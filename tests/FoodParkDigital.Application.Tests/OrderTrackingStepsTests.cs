using FoodParkDigital.Application.Orders;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The steps a customer follows the order by (Fase 24C, ADR 0023): those of its kind, the first time it reached each one,
/// the ones the business skipped done without a time, «En camino» only for an order to an address, and a cancelled order
/// up to where it got.
/// </summary>
public class OrderTrackingStepsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 4, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public void KitchenOrder_InPreparation_ShouldBeOnItsThirdStep_WithoutOnTheWay()
    {
        var steps = OrderTracking.Steps(BusinessType.Food, OrderType.Retail, OrderStatus.InPreparation, T0,
            [Change(OrderStatus.Pending, OrderStatus.Confirmed, 2), Change(OrderStatus.Confirmed, OrderStatus.InPreparation, 5)],
            hasDelivery: false, dispatchedAt: null);

        Assert.Equal(
        [
            ("Recibido", OrderTrackingStepState.Done, T0),
            ("Confirmado", OrderTrackingStepState.Done, T0.AddMinutes(2)),
            ("En preparación", OrderTrackingStepState.Current, T0.AddMinutes(5)),
            ("Listo", OrderTrackingStepState.Upcoming, (DateTimeOffset?)null),
            ("Entregado", OrderTrackingStepState.Upcoming, null)
        ], Shape(steps));
    }

    [Fact]
    public void OrderToAnAddress_ThatWentOut_ShouldBeOnTheWay_WithTheTimeItLeft()
    {
        var history = new[]
        {
            Change(OrderStatus.Pending, OrderStatus.Confirmed, 1),
            Change(OrderStatus.Confirmed, OrderStatus.InPreparation, 3),
            Change(OrderStatus.InPreparation, OrderStatus.Ready, 15)
        };

        var steps = OrderTracking.Steps(BusinessType.Food, OrderType.Retail, OrderStatus.Ready, T0, history,
            hasDelivery: true, dispatchedAt: T0.AddMinutes(20));

        Assert.Equal(
        [
            ("Recibido", OrderTrackingStepState.Done, T0),
            ("Confirmado", OrderTrackingStepState.Done, T0.AddMinutes(1)),
            ("En preparación", OrderTrackingStepState.Done, T0.AddMinutes(3)),
            ("Listo", OrderTrackingStepState.Done, T0.AddMinutes(15)),
            ("En camino", OrderTrackingStepState.Current, T0.AddMinutes(20)),
            ("Entregado", OrderTrackingStepState.Upcoming, (DateTimeOffset?)null)
        ], Shape(steps));
        Assert.Equal((OrderTrackingStep.OnTheWay, "En camino"),
            OrderTracking.Current(BusinessType.Food, OrderType.Retail, OrderStatus.Ready, true, T0.AddMinutes(20)));
        // Waiting for the courier, it is still ready; delivered, it is no longer on its way.
        Assert.Equal((OrderTrackingStep.Ready, "Listo"),
            OrderTracking.Current(BusinessType.Food, OrderType.Retail, OrderStatus.Ready, true, null));
        Assert.Equal((OrderTrackingStep.Delivered, "Entregado"),
            OrderTracking.Current(BusinessType.Food, OrderType.Retail, OrderStatus.Delivered, true, T0.AddMinutes(20)));
    }

    [Fact]
    public void StoreOrder_DeliveredStraightAway_ShouldShowEveryStepDone_TheSkippedOnesWithoutATime()
    {
        var steps = OrderTracking.Steps(BusinessType.Retail, OrderType.Retail, OrderStatus.Delivered, T0,
            [Change(OrderStatus.Pending, OrderStatus.Delivered, 30)], hasDelivery: true, dispatchedAt: null);

        Assert.Equal(
        [
            ("Recibido", OrderTrackingStepState.Done, T0),
            ("Confirmado", OrderTrackingStepState.Done, (DateTimeOffset?)null),
            ("En camino", OrderTrackingStepState.Done, null),
            ("Entregado", OrderTrackingStepState.Done, T0.AddMinutes(30))
        ], Shape(steps));
    }

    [Fact]
    public void Service_ShouldBeAcceptedDoneAndFinished()
    {
        var steps = OrderTracking.Steps(BusinessType.Service, OrderType.Service, OrderStatus.Completed, T0,
        [
            Change(OrderStatus.Pending, OrderStatus.Accepted, 10),
            Change(OrderStatus.Accepted, OrderStatus.InProgress, 60),
            Change(OrderStatus.InProgress, OrderStatus.Completed, 180)
        ], hasDelivery: false, dispatchedAt: null);

        Assert.Equal(
        [
            ("Recibido", OrderTrackingStepState.Done, T0),
            ("Aceptado", OrderTrackingStepState.Done, T0.AddMinutes(10)),
            ("En proceso", OrderTrackingStepState.Done, T0.AddMinutes(60)),
            ("Terminado", OrderTrackingStepState.Done, (DateTimeOffset?)T0.AddMinutes(180))
        ], Shape(steps));
        // Finished, nothing is left to happen: no step is the current one.
        Assert.DoesNotContain(steps, s => s.State == OrderTrackingStepState.Current);
    }

    [Fact]
    public void State_ThatComesBack_ShouldKeepTheFirstTimeItReachedTheStep()
    {
        var steps = OrderTracking.Steps(BusinessType.Food, OrderType.Retail, OrderStatus.InProgress, T0,
        [
            Change(OrderStatus.Pending, OrderStatus.Confirmed, 1),
            Change(OrderStatus.Confirmed, OrderStatus.Accepted, 2),
            Change(OrderStatus.Accepted, OrderStatus.Confirmed, 3),
            Change(OrderStatus.Confirmed, OrderStatus.InPreparation, 4),
            Change(OrderStatus.InPreparation, OrderStatus.InProgress, 6),
            // The creation of the order is in its history too, without a change.
            Change(OrderStatus.Pending, OrderStatus.Pending, 0)
        ], hasDelivery: false, dispatchedAt: null);

        Assert.Equal(T0.AddMinutes(1), steps.Single(s => s.Step == OrderTrackingStep.Confirmed).At);
        Assert.Equal((T0.AddMinutes(4), OrderTrackingStepState.Current),
            (steps.Single(s => s.Step == OrderTrackingStep.Preparing).At, steps.Single(s => s.Step == OrderTrackingStep.Preparing).State));
    }

    [Fact]
    public void CancelledOrder_ShouldShowTheStepsItReached_AndThenCancelled()
    {
        var confirmed = OrderTracking.Steps(BusinessType.Food, OrderType.Retail, OrderStatus.Cancelled, T0,
            [Change(OrderStatus.Pending, OrderStatus.Confirmed, 2), Change(OrderStatus.Confirmed, OrderStatus.Cancelled, 10)],
            hasDelivery: false, dispatchedAt: null);
        var waiting = OrderTracking.Steps(BusinessType.Retail, OrderType.Retail, OrderStatus.Cancelled, T0,
            [Change(OrderStatus.Pending, OrderStatus.Cancelled, 4)], hasDelivery: true, dispatchedAt: null);

        Assert.Equal(
        [
            ("Recibido", OrderTrackingStepState.Done, T0),
            ("Confirmado", OrderTrackingStepState.Done, T0.AddMinutes(2)),
            ("Cancelado", OrderTrackingStepState.Current, (DateTimeOffset?)T0.AddMinutes(10))
        ], Shape(confirmed));
        Assert.Equal(
        [
            ("Recibido", OrderTrackingStepState.Done, T0),
            ("Cancelado", OrderTrackingStepState.Current, (DateTimeOffset?)T0.AddMinutes(4))
        ], Shape(waiting));
        Assert.Equal((OrderTrackingStep.Cancelled, "Cancelado"),
            OrderTracking.Current(BusinessType.Retail, OrderType.Retail, OrderStatus.Cancelled, true, null));
    }

    [Fact]
    public void OrderCancelledOnItsWay_ShouldShowThatItHadGoneOut()
    {
        var steps = OrderTracking.Steps(BusinessType.Food, OrderType.Retail, OrderStatus.Cancelled, T0,
        [
            Change(OrderStatus.Pending, OrderStatus.Confirmed, 1),
            Change(OrderStatus.Confirmed, OrderStatus.InPreparation, 5),
            Change(OrderStatus.InPreparation, OrderStatus.Cancelled, 12)
        ], hasDelivery: true, dispatchedAt: T0.AddMinutes(8));

        Assert.Equal(
        [
            ("Recibido", OrderTrackingStepState.Done, T0),
            ("Confirmado", OrderTrackingStepState.Done, T0.AddMinutes(1)),
            ("En preparación", OrderTrackingStepState.Done, T0.AddMinutes(5)),
            ("Listo", OrderTrackingStepState.Done, (DateTimeOffset?)null),
            ("En camino", OrderTrackingStepState.Done, T0.AddMinutes(8)),
            ("Cancelado", OrderTrackingStepState.Current, T0.AddMinutes(12))
        ], Shape(steps));
    }

    [Fact]
    public void Order_ShouldBeFollowedWhileOpen_ForItsDays()
    {
        Assert.True(OrderTracking.IsInProgress(OrderStatus.Pending, T0.AddDays(-OrderTracking.Days), T0));
        Assert.False(OrderTracking.IsInProgress(OrderStatus.Pending, T0.AddDays(-OrderTracking.Days).AddMinutes(-1), T0));
        Assert.False(OrderTracking.IsInProgress(OrderStatus.Delivered, T0, T0));
        Assert.False(OrderTracking.IsInProgress(OrderStatus.Completed, T0, T0));
        Assert.False(OrderTracking.IsInProgress(OrderStatus.Cancelled, T0, T0));

        Assert.True(OrderTracking.IsOnTheWay(OrderStatus.Ready, T0));
        Assert.False(OrderTracking.IsOnTheWay(OrderStatus.Ready, null));
        Assert.False(OrderTracking.IsOnTheWay(OrderStatus.Delivered, T0));
    }

    [Fact]
    public void NewToken_ShouldHaveTheShapeOfALink_AndBeAnotherEachTime()
    {
        var tokens = Enumerable.Range(0, 50).Select(_ => OrderTracking.NewToken()).ToList();

        Assert.All(tokens, token => Assert.True(Order.IsTrackingToken(token), token));
        Assert.Equal(tokens.Count, tokens.Distinct(StringComparer.Ordinal).Count());
    }

    private static OrderStatusChange Change(OrderStatus from, OrderStatus to, int minutes) => new(from, to, T0.AddMinutes(minutes));

    private static List<(string Label, OrderTrackingStepState State, DateTimeOffset? At)> Shape(List<OrderTrackingStepDto> steps)
        => steps.Select(step => (step.Label, step.State, step.At)).ToList();
}
