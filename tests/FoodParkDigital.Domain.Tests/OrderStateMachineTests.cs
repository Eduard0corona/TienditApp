using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;

namespace FoodParkDigital.Domain.Tests;

public class OrderStateMachineTests
{
    [Fact]
    public void FoodStateMachine_ShouldAllowConfirmedToAccepted()
    {
        var stateMachine = new FoodOrderStateMachine();

        var allowed = stateMachine.GetAllowedTransitions(OrderStatus.Confirmed);

        Assert.Contains(OrderStatus.Accepted, allowed);
        Assert.Contains(OrderStatus.InPreparation, allowed);
    }

    [Fact]
    public void ServiceStateMachine_ShouldRejectInProgressToReady()
    {
        var stateMachine = new ServiceOrderStateMachine();

        Assert.Throws<InvalidOrderStateTransitionException>(
            () => stateMachine.ValidateTransition(OrderStatus.InProgress, OrderStatus.Ready));
    }

    [Fact]
    public void RetailStateMachine_ShouldExposeExpectedInitialAndCancellationRules()
    {
        var stateMachine = new RetailOrderStateMachine();

        Assert.Equal(OrderStatus.Pending, stateMachine.GetInitialStatus());
        Assert.True(stateMachine.CanCancel(OrderStatus.Accepted));
        Assert.False(stateMachine.CanCancel(OrderStatus.Delivered));
    }
}
