using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Orders.Entities;

public class Order : AuditableEntity, ITenantScoped, IAggregateRoot
{
    private readonly List<OrderItem> _items = new();
    private readonly List<OrderStatusHistory> _statusHistory = new();

    private Order() { }

    public Order(
        Guid tenantId,
        Guid? customerUserId,
        Guid? temporaryCustomerId,
        bool createdByBusiness,
        string orderNumber,
        BusinessType businessType,
        string customerName,
        string? customerPhone,
        string? customerNotes,
        string? tableNumber)
    {
        TenantId = tenantId;
        CustomerUserId = customerUserId;
        TemporaryCustomerId = temporaryCustomerId;
        CreatedByBusiness = createdByBusiness;

        if (CustomerUserId.HasValue == TemporaryCustomerId.HasValue)
            throw new DomainException("Order must reference either a registered customer or a temporary customer.");

        OrderNumber = orderNumber;
        BusinessType = businessType;
        CustomerName = customerName;
        CustomerPhone = customerPhone;
        CustomerNotes = customerNotes;
        TableNumber = tableNumber;

        var stateMachine = OrderStateMachineFactory.Create(businessType);
        Status = stateMachine.GetInitialStatus();

        _statusHistory.Add(new OrderStatusHistory(Id, Status, Status, null));
    }

    public Guid TenantId { get; private set; }
    public Guid? CustomerUserId { get; private set; }
    public Guid? TemporaryCustomerId { get; private set; }
    public bool CreatedByBusiness { get; private set; }
    public string OrderNumber { get; private set; } = null!;
    public BusinessType BusinessType { get; private set; }
    public OrderStatus Status { get; private set; }
    public string CustomerName { get; private set; } = null!;
    public string? CustomerPhone { get; private set; }
    public string? CustomerNotes { get; private set; }
    public string? TableNumber { get; private set; }
    public decimal SubTotal { get; private set; }
    public decimal Tax { get; private set; }
    public decimal Total { get; private set; }
    public string? CancellationReason { get; private set; }
    public byte[] Version { get; private set; } = null!;

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<OrderStatusHistory> StatusHistory => _statusHistory.AsReadOnly();

    public void AddItem(Guid productId, string productName, int quantity, decimal unitPrice, string? notes = null)
    {
        if (Status != OrderStatus.Pending)
            throw new DomainException("Cannot add items to an order that is not pending.");

        var item = new OrderItem(Id, productId, productName, quantity, unitPrice, notes);
        _items.Add(item);
        RecalculateTotals();
    }

    public void TransitionTo(OrderStatus newStatus, IOrderStateMachine stateMachine, string? performedBy)
    {
        stateMachine.ValidateTransition(Status, newStatus);
        var previousStatus = Status;
        Status = newStatus;
        _statusHistory.Add(new OrderStatusHistory(Id, previousStatus, newStatus, performedBy));
        AddDomainEvent(new OrderStatusChangedEvent(Id, TenantId, previousStatus, newStatus));
    }

    public void Cancel(IOrderStateMachine stateMachine, string? performedBy, string? reason = null)
    {
        if (!stateMachine.CanCancel(Status))
            throw new InvalidOrderStateTransitionException(Status, OrderStatus.Cancelled, BusinessType);

        var previousStatus = Status;
        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        _statusHistory.Add(new OrderStatusHistory(Id, previousStatus, OrderStatus.Cancelled, performedBy));
        AddDomainEvent(new OrderCancelledEvent(Id, TenantId, previousStatus, reason));
    }

    public void MarkAsCreated()
    {
        AddDomainEvent(new OrderCreatedEvent(Id, TenantId, OrderNumber, BusinessType, Total));
    }

    public void LinkToCustomerUser(Guid userId)
    {
        CustomerUserId = userId;
        TemporaryCustomerId = null;
    }

    private void RecalculateTotals()
    {
        SubTotal = _items.Sum(i => i.TotalPrice);
        Tax = Math.Round(SubTotal * 0.16m, 2);
        Total = SubTotal + Tax;
    }
}
