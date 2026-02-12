using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Exceptions;

public class InvalidOrderStateTransitionException : DomainException
{
    public OrderStatus FromStatus { get; }
    public OrderStatus ToStatus { get; }
    public BusinessType BusinessType { get; }

    public InvalidOrderStateTransitionException(OrderStatus from, OrderStatus to, BusinessType businessType)
        : base($"Invalid order state transition from '{from}' to '{to}' for business type '{businessType}'.")
    {
        FromStatus = from;
        ToStatus = to;
        BusinessType = businessType;
    }
}
