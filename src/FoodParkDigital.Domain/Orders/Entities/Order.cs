using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Orders.Taxes;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Orders.Entities;

public class Order : AuditableEntity, ITenantScoped, IAggregateRoot
{
    private readonly List<OrderItem> _items = new();
    private readonly List<OrderStatusHistory> _statusHistory = new();
    private readonly List<OrderPayment> _payments = new();
    private readonly List<ServiceOrderPiece> _servicePieces = new();

    private Order() { }

    public Order(
        Guid tenantId,
        Guid? customerUserId,
        Guid? temporaryCustomerId,
        bool createdByBusiness,
        string orderNumber,
        BusinessType businessType,
        OrderType orderType,
        string customerName,
        string? customerPhone,
        string? customerNotes,
        string? tableNumber,
        bool pricesIncludeTax = false)
    {
        TenantId = tenantId;
        CustomerUserId = customerUserId;
        TemporaryCustomerId = temporaryCustomerId;
        CreatedByBusiness = createdByBusiness;

        if (CustomerUserId.HasValue && TemporaryCustomerId.HasValue)
            throw new DomainException("La orden no puede referir a un cliente registrado y a uno temporal a la vez.");

        // A counter sale to the general public ("público en general") has no phone and therefore no
        // customer to link; an order placed by a customer always references one.
        if (!CustomerUserId.HasValue && !TemporaryCustomerId.HasValue && !createdByBusiness)
            throw new DomainException("La orden de un cliente debe referir a un cliente registrado o temporal.");

        OrderNumber = orderNumber;
        BusinessType = businessType;
        OrderType = orderType;
        CustomerName = customerName;
        CustomerPhone = customerPhone;
        CustomerNotes = customerNotes;
        TableNumber = tableNumber;
        // The business policy at the time of the sale: later changes to it never alter this order.
        PricesIncludeTax = pricesIncludeTax;

        var stateMachine = OrderStateMachineFactory.Create(businessType, orderType);
        Status = stateMachine.GetInitialStatus();

        _statusHistory.Add(new OrderStatusHistory(Id, Status, Status, null));
    }

    public Guid TenantId { get; private set; }
    public Guid? CustomerUserId { get; private set; }
    public Guid? TemporaryCustomerId { get; private set; }
    public bool CreatedByBusiness { get; private set; }
    public string OrderNumber { get; private set; } = null!;
    public BusinessType BusinessType { get; private set; }
    public OrderType OrderType { get; private set; }
    public OrderStatus Status { get; private set; }
    public string CustomerName { get; private set; } = null!;
    public string? CustomerPhone { get; private set; }
    public string? CustomerNotes { get; private set; }
    public string? TableNumber { get; private set; }
    /// <summary>True when item prices already include IVA; false when IVA is added on top of them.</summary>
    public bool PricesIncludeTax { get; private set; }
    /// <summary>Taxable base: item amounts without IVA.</summary>
    public decimal SubTotal { get; private set; }
    public decimal Tax { get; private set; }
    public decimal Total { get; private set; }
    public decimal PaidAmount { get; private set; }
    public decimal Balance => Total - PaidAmount;
    public string? CancellationReason { get; private set; }
    public byte[] Version { get; private set; } = null!;
    public ServiceOrderDetail? ServiceDetail { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<OrderStatusHistory> StatusHistory => _statusHistory.AsReadOnly();
    public IReadOnlyCollection<OrderPayment> Payments => _payments.AsReadOnly();
    public IReadOnlyCollection<ServiceOrderPiece> ServicePieces => _servicePieces.AsReadOnly();

    /// <param name="vatRate">IVA of the line; the general rate (16 %) when omitted.</param>
    public void AddItem(
        Guid productId,
        string productName,
        int quantity,
        decimal unitPrice,
        string? notes = null,
        VatRate? vatRate = null)
    {
        if (Status != OrderStatus.Pending)
            throw new DomainException("Cannot add items to an order that is not pending.");

        var item = new OrderItem(Id, productId, productName, quantity, unitPrice, notes, vatRate ?? VatRate.General);
        _items.Add(item);
        RecalculateTotals();
    }

    public void SetServiceDetail(
        DateTimeOffset receivedAt,
        DateTimeOffset? promisedAt,
        int priority,
        string? intakeNotes,
        string? deliveryNotes,
        string? intakeChecklistJson,
        string? beforePhotosJson,
        string? afterPhotosJson)
    {
        if (OrderType != OrderType.Service)
            throw new DomainException("Solo las ordenes de servicio pueden tener detalle de servicio.");

        if (ServiceDetail is null)
        {
            ServiceDetail = new ServiceOrderDetail(
                tenantId: TenantId,
                orderId: Id,
                receivedAt: receivedAt,
                promisedAt: promisedAt,
                priority: priority,
                intakeNotes: intakeNotes,
                deliveryNotes: deliveryNotes,
                intakeChecklistJson: intakeChecklistJson,
                beforePhotosJson: beforePhotosJson,
                afterPhotosJson: afterPhotosJson);
            return;
        }

        ServiceDetail.Update(
            promisedAt: promisedAt,
            priority: priority,
            intakeNotes: intakeNotes,
            deliveryNotes: deliveryNotes,
            intakeChecklistJson: intakeChecklistJson,
            beforePhotosJson: beforePhotosJson,
            afterPhotosJson: afterPhotosJson);
    }

    public void AddServicePiece(
        string itemName,
        string? brand,
        string? model,
        string? color,
        string? size,
        string? material,
        string? conditionNotes,
        string? requestedService,
        string? internalNotes,
        Guid? serviceProductId = null,
        bool isTrackable = true,
        bool notifyCustomer = false)
    {
        _servicePieces.Add(new ServiceOrderPiece(
            tenantId: TenantId,
            orderId: Id,
            itemName: itemName,
            brand: brand,
            model: model,
            color: color,
            size: size,
            material: material,
            conditionNotes: conditionNotes,
            requestedService: requestedService,
            internalNotes: internalNotes,
            serviceProductId: serviceProductId,
            isTrackable: isTrackable,
            notifyCustomer: notifyCustomer));
    }

    /// <summary>
    /// Changes a piece through the aggregate root. The order row is marked as changed so the save checks
    /// its row version: two pieces changed at the same time then conflict (and the loser retries)
    /// instead of each one deciding the order status from a stale view of the other piece.
    /// </summary>
    public void ChangeServicePieceStatus(ServiceOrderPiece piece, ServiceOrderPieceStatus newStatus, DateTimeOffset changedAt)
    {
        if (!_servicePieces.Contains(piece))
            throw new DomainException("La pieza indicada no existe dentro de la orden.");

        piece.UpdateStatus(newStatus);
        UpdatedAt = changedAt;
    }

    public void TransitionTo(OrderStatus newStatus, IOrderStateMachine stateMachine, string? performedBy)
        => Transition(newStatus, stateMachine, performedBy, notifyCustomer: true);

    /// <summary>
    /// Closes a fully paid counter sale: the customer leaves with what they bought, so nobody has to mark
    /// it delivered by hand. Food that goes through the kitchen closes only once it is ready, and a service
    /// only when none of its tracked pieces is still being worked on (those close when their pieces are
    /// completed). The customer is not messaged about it: they are at the counter.
    /// </summary>
    /// <returns>True when the order was closed.</returns>
    public bool CloseIfPaidCounterSale(IOrderStateMachine stateMachine, bool waitsForKitchen, string? performedBy)
    {
        if (!CreatedByBusiness || _items.Count == 0 || Balance > 0)
            return false;

        OrderStatus closedStatus;
        if (OrderType == OrderType.Service)
        {
            if (_servicePieces.Any(piece => piece.IsTrackable
                    && piece.Status is ServiceOrderPieceStatus.Accepted or ServiceOrderPieceStatus.InProgress))
                return false;

            closedStatus = OrderStatus.Completed;
        }
        else
        {
            if (waitsForKitchen && Status != OrderStatus.Ready)
                return false;

            closedStatus = OrderStatus.Delivered;
        }

        if (!stateMachine.GetAllowedTransitions(Status).Contains(closedStatus))
            return false;

        Transition(closedStatus, stateMachine, performedBy, notifyCustomer: false);
        return true;
    }

    private void Transition(OrderStatus newStatus, IOrderStateMachine stateMachine, string? performedBy, bool notifyCustomer)
    {
        stateMachine.ValidateTransition(Status, newStatus);
        var previousStatus = Status;
        Status = newStatus;
        _statusHistory.Add(new OrderStatusHistory(Id, previousStatus, newStatus, performedBy));
        AddDomainEvent(new OrderStatusChangedEvent(Id, TenantId, previousStatus, newStatus, notifyCustomer));
    }

    public void Cancel(IOrderStateMachine stateMachine, string? performedBy, string? reason = null)
    {
        if (!stateMachine.CanCancel(Status))
            throw new InvalidOrderStateTransitionException(Status, OrderStatus.Cancelled, BusinessType);

        // Until refunds are modeled (see ADR 0007), cancelling would leave payments and cash
        // movements counted for an order that no longer exists.
        if (PaidAmount > 0)
            throw new DomainException(
                "La orden tiene pagos registrados; no se puede cancelar hasta registrar el reembolso.");

        var previousStatus = Status;
        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        _statusHistory.Add(new OrderStatusHistory(Id, previousStatus, OrderStatus.Cancelled, performedBy));

        // Pieces still pending or in progress belong to an order that no longer exists: they leave the
        // service board and can no longer be completed (which would consume inventory).
        foreach (var piece in _servicePieces.Where(p => p.Status is ServiceOrderPieceStatus.Accepted or ServiceOrderPieceStatus.InProgress))
            piece.UpdateStatus(ServiceOrderPieceStatus.Cancelled);

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

    public OrderPayment RegisterPayment(
        decimal amount,
        OrderPaymentMethod method,
        DateTimeOffset paidAt,
        Guid? inventoryLocationId,
        string? evidenceUrl,
        string? reference,
        string? notes)
    {
        var payment = new OrderPayment(
            tenantId: TenantId,
            orderId: Id,
            amount: amount,
            method: method,
            paidAt: paidAt,
            inventoryLocationId: inventoryLocationId,
            evidenceUrl: evidenceUrl,
            reference: reference,
            notes: notes);

        if (Status == OrderStatus.Cancelled)
            throw new DomainException("No se pueden registrar pagos en una orden cancelada.");

        var newPaidAmount = PaidAmount + payment.Amount;
        if (newPaidAmount > Total)
            throw new DomainException("El pago excede el total de la orden.");

        _payments.Add(payment);
        PaidAmount = newPaidAmount;
        return payment;
    }

    /// <summary>IVA by rate, computed from the items and the policy the order was sold with.</summary>
    public IReadOnlyList<OrderTaxLine> GetTaxBreakdown()
        => OrderTaxCalculator.Calculate(
            PricesIncludeTax,
            _items.Select(item => new TaxableAmount(item.TotalPrice, item.VatRate)));

    private void RecalculateTotals()
    {
        var breakdown = GetTaxBreakdown();
        SubTotal = breakdown.Sum(line => line.TaxableBase);
        Tax = breakdown.Sum(line => line.TaxAmount);
        Total = SubTotal + Tax;
        if (PaidAmount > Total)
            PaidAmount = Total;
    }
}
