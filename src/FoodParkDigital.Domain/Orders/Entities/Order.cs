using System.Globalization;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Orders.Taxes;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Orders.Entities;

public class Order : AuditableEntity, ITenantScoped, IAggregateRoot
{
    private readonly List<OrderItem> _items = new();
    private readonly List<OrderStatusHistory> _statusHistory = new();
    private readonly List<OrderPayment> _payments = new();
    private readonly List<OrderRefund> _refunds = new();
    private readonly List<ServiceOrderPiece> _servicePieces = new();
    private readonly List<KitchenTicket> _kitchenTickets = new();
    private readonly List<OrderItemCancellation> _itemCancellations = new();

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
        bool pricesIncludeTax = false,
        OrderChannel channel = OrderChannel.Counter,
        Guid? branchId = null)
    {
        // An order of a delivery platform comes in through the platform's app: the business registers it, and the
        // app shares neither the customer nor their phone. Blank phone and table are stored as none.
        if (OrderChannelRules.IsDeliveryPlatform(channel))
        {
            if (!createdByBusiness)
                throw new DomainException(PlatformOrderRules.RegisteredByBusinessMessage);
            if (customerUserId.HasValue || temporaryCustomerId.HasValue)
                throw new DomainException(PlatformOrderRules.NoCustomerMessage);
            if (!string.IsNullOrWhiteSpace(customerPhone))
                throw new DomainException(PlatformOrderRules.NoPhoneMessage);
            if (!string.IsNullOrWhiteSpace(tableNumber))
                throw new DomainException(PlatformOrderRules.NoTableMessage);
            if (orderType != OrderType.Retail)
                throw new DomainException(PlatformOrderRules.OnlyProductsMessage);

            customerPhone = null;
            tableNumber = null;
        }

        TenantId = tenantId;
        BranchId = branchId;
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
        Channel = Enum.IsDefined(channel) ? channel : throw new DomainException(OrderChannelRules.InvalidChannelMessage);

        var stateMachine = OrderStateMachineFactory.Create(businessType, orderType);
        Status = stateMachine.GetInitialStatus();

        _statusHistory.Add(new OrderStatusHistory(TenantId, Id, Status, Status, null));
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
    /// <summary>
    /// Where the order came from, fixed when the order is created and never changed: sales by channel, the order list
    /// filter and the rules of the delivery platforms (no customer, no notices, the «Plataforma» payment, the order's
    /// <see cref="PlatformDetail"/>) rely on it never moving.
    /// </summary>
    public OrderChannel Channel { get; private set; }
    /// <summary>
    /// Branch where the sale was made, fixed when the order is created: reports by branch and the daily sales
    /// snapshots rely on it never moving.
    /// </summary>
    public Guid? BranchId { get; private set; }
    /// <summary>
    /// Key the device sent with the sale: a retry with the same key (a sale captured without connection and sent
    /// later, or a request repeated after a timeout) returns this order instead of creating another one.
    /// </summary>
    public Guid? ClientRequestId { get; private set; }
    /// <summary>When the device captured the sale without connection (its own clock; payments keep the server time).</summary>
    public DateTimeOffset? CapturedOfflineAt { get; private set; }
    /// <summary>Taxable base: item amounts without IVA.</summary>
    public decimal SubTotal { get; private set; }
    public decimal Tax { get; private set; }
    public decimal Total { get; private set; }
    public decimal PaidAmount { get; private set; }

    /// <summary>Money given back. Payments stay as they were: refunds compensate them.</summary>
    public decimal RefundedAmount { get; private set; }

    /// <summary>What the business kept from this order: paid minus refunded.</summary>
    public decimal NetPaidAmount => PaidAmount - RefundedAmount;
    public decimal Balance => Total - PaidAmount;
    /// <summary>Tips given with the payments: on top of the sale, never part of its total or balance.</summary>
    public decimal TipTotal => _payments.Sum(p => p.Tip);
    public string? CancellationReason { get; private set; }
    public byte[] Version { get; private set; } = null!;
    public ServiceOrderDetail? ServiceDetail { get; private set; }

    /// <summary>
    /// The folio, the terms and the amounts an order of a delivery platform was registered with
    /// (<see cref="RegisterPlatformOrder"/>); none for any other channel. Loaded only when a query includes it.
    /// </summary>
    public PlatformOrderDetail? PlatformDetail { get; private set; }

    /// <summary>The order came from Rappi, Uber Eats or DiDi Food. It depends on the channel, which is always loaded.</summary>
    public bool IsDeliveryPlatformOrder => OrderChannelRules.IsDeliveryPlatform(Channel);

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<OrderStatusHistory> StatusHistory => _statusHistory.AsReadOnly();
    public IReadOnlyCollection<OrderPayment> Payments => _payments.AsReadOnly();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();
    public IReadOnlyCollection<ServiceOrderPiece> ServicePieces => _servicePieces.AsReadOnly();
    /// <summary>Rounds of an open tab sent to the kitchen.</summary>
    public IReadOnlyCollection<KitchenTicket> KitchenTickets => _kitchenTickets.AsReadOnly();
    /// <summary>Dishes of the tab cancelled after they went to the kitchen (<see cref="CancelTabItem"/>).</summary>
    public IReadOnlyCollection<OrderItemCancellation> ItemCancellations => _itemCancellations.AsReadOnly();

    /// <summary>The order is (or was) a tab: items are added in rounds and it is charged when the table leaves.</summary>
    public bool IsTab { get; private set; }
    public Guid? DiningTableId { get; private set; }
    /// <summary>The table while the tab is open; cleared when it closes, so a table has one open tab at most.</summary>
    public Guid? OpenTableId { get; private set; }

    public bool IsOpenTab => IsTab && !IsClosedStatus(Status);

    /// <summary>Records the device's key of the sale (once) and, for a sale captured without connection, when it was captured.</summary>
    public void IdentifyClientRequest(Guid clientRequestId, DateTimeOffset? capturedOfflineAt)
    {
        if (clientRequestId == Guid.Empty)
            throw new DomainException("La clave de la venta no es válida.");
        if (ClientRequestId.HasValue && ClientRequestId != clientRequestId)
            throw new DomainException("La venta ya tiene otra clave.");
        ClientRequestId = clientRequestId;
        CapturedOfflineAt = capturedOfflineAt;
    }
    public int CurrentRound => _items.Count == 0 ? 0 : _items.Max(item => item.Round);

    /// <param name="unitPrice">Price of one unit with the chosen options.</param>
    /// <param name="vatRate">IVA of the line; the general rate (16 %) when omitted.</param>
    /// <param name="fiscal">Unit and invoicing codes copied from the product; a generic piece when omitted.</param>
    /// <param name="options">Variants and extras chosen, with the price each one added.</param>
    public void AddItem(
        Guid productId,
        string productName,
        decimal quantity,
        decimal unitPrice,
        string? notes = null,
        VatRate? vatRate = null,
        OrderLineFiscalData? fiscal = null,
        IReadOnlyList<OrderLineOption>? options = null)
    {
        if (Status != OrderStatus.Pending)
            throw new DomainException("Solo se agregan productos a un pedido pendiente.");
        // Its commission and expected settlement were calculated on the total it was registered with.
        if (PlatformDetail is not null)
            throw new DomainException(PlatformOrderRules.NoItemsAfterRegisteredMessage);

        _items.Add(new OrderItem(Id, productId, productName, quantity, unitPrice, notes, vatRate ?? VatRate.General, fiscal, options, TenantId));
        RecalculateTotals();
    }

    /// <summary>Adds a line priced and checked against the catalog (see <see cref="OrderLineRequest"/>).</summary>
    public void AddItem(OrderLineRequest line)
        => AddItem(line.ProductId, line.ProductName, line.Quantity, line.UnitPrice, line.Notes, line.VatRate, line.Fiscal, line.Options);

    /// <summary>
    /// Turns a new order into an open tab: its items are the first round, sent to the kitchen now. Called before
    /// the order leaves Pending.
    /// </summary>
    public void OpenAsTab(Guid? diningTableId, string? tableName, DateTimeOffset sentAt, string? sentBy)
    {
        if (IsDeliveryPlatformOrder)
            throw new DomainException(PlatformOrderRules.NoTabMessage);
        if (IsTab || Status != OrderStatus.Pending)
            throw new DomainException("Solo un pedido nuevo puede abrirse como cuenta.");
        if (OrderType != OrderType.Retail)
            throw new DomainException("Una cuenta abierta es de consumo; los servicios no se abren como cuenta.");
        if (_items.Count == 0)
            throw new DomainException("La cuenta se abre con la primera ronda.");

        IsTab = true;
        DiningTableId = diningTableId;
        OpenTableId = diningTableId;
        if (!string.IsNullOrWhiteSpace(tableName))
            TableNumber = tableName.Trim();
        _kitchenTickets.Add(new KitchenTicket(TenantId, Id, 1, sentAt, sentBy));
    }

    /// <summary>Adds a round to an open tab and sends it to the kitchen.</summary>
    public KitchenTicket AddRound(IReadOnlyList<OrderLineRequest> lines, DateTimeOffset sentAt, string? sentBy)
    {
        if (!IsOpenTab)
            throw new DomainException("La cuenta ya está cerrada: abre una nueva.");
        if (lines.Count == 0)
            throw new DomainException("La ronda necesita al menos un producto.");

        var round = CurrentRound + 1;
        foreach (var line in lines)
        {
            _items.Add(new OrderItem(
                Id, line.ProductId, line.ProductName, line.Quantity, line.UnitPrice, line.Notes, line.VatRate, line.Fiscal,
                line.Options, TenantId, round));
        }

        var ticket = new KitchenTicket(TenantId, Id, round, sentAt, sentBy);
        _kitchenTickets.Add(ticket);
        RecalculateTotals();
        UpdatedAt = sentAt;
        AddDomainEvent(new TabRoundSentEvent(Id, TenantId, round));
        return ticket;
    }

    /// <summary>Moves a round forward in the kitchen (preparing, ready, served).</summary>
    public void ChangeKitchenTicketStatus(Guid ticketId, KitchenTicketStatus status, DateTimeOffset at)
    {
        var ticket = _kitchenTickets.FirstOrDefault(t => t.Id == ticketId)
            ?? throw new DomainException("La ronda indicada no pertenece a esta cuenta.");
        if (!IsOpenTab)
            throw new DomainException("La cuenta ya está cerrada.");

        ticket.MoveTo(status, at);
        // Marks the order row as changed so two cooks moving the same round conflict instead of overwriting.
        UpdatedAt = at;
        AddDomainEvent(new KitchenTicketChangedEvent(Id, TenantId, ticket.Round, status));
    }

    /// <summary>
    /// Cancels units of a dish of an open tab that already went to the kitchen: the tab stops charging them. The line
    /// keeps what was ordered and the cancellation is its own record, with the reason, who and when. Only what is not
    /// paid yet can be cancelled: a dish already paid is refunded instead.
    /// </summary>
    /// <param name="returnToStock">Its supplies go back to stock (the kitchen did not prepare it); otherwise they were lost.</param>
    public OrderItemCancellation CancelTabItem(
        Guid orderItemId,
        decimal quantity,
        string reason,
        bool returnToStock,
        DateTimeOffset cancelledAt,
        string cancelledBy)
    {
        if (!IsOpenTab)
            throw new DomainException("La cuenta ya está cerrada.");
        var item = _items.FirstOrDefault(i => i.Id == orderItemId)
            ?? throw new DomainException("El platillo indicado no pertenece a esta cuenta.");

        var amount = item.CancellationAmount(quantity);
        var (subTotal, tax) = ComputeTotals(line => line == item ? line.ChargedPrice - amount : line.ChargedPrice);
        if (subTotal + tax < PaidAmount)
        {
            throw new DomainException(
                $"La cuenta ya tiene cobrados ${PaidAmount.ToString("N2", CultureInfo.InvariantCulture)} y sin ese platillo "
                + $"quedaría en ${(subTotal + tax).ToString("N2", CultureInfo.InvariantCulture)}: reembólsalo en lugar de cancelarlo.");
        }

        var cancellation = new OrderItemCancellation(
            TenantId, Id, item.Id, item.Round, quantity, amount, reason, returnToStock, cancelledAt, cancelledBy);
        item.RegisterCancellation(quantity, amount);
        _itemCancellations.Add(cancellation);
        RecalculateTotals();
        // Marks the order row as changed: two devices changing the tab at the same time conflict instead of overwriting.
        UpdatedAt = cancelledAt;
        AddDomainEvent(new TabItemCancelledEvent(
            Id, TenantId, cancellation.Id, item.Id, item.Round, item.Describe(), quantity, amount, cancellation.Reason, returnToStock));
        return cancellation;
    }

    /// <summary>Moves an open tab to another free table.</summary>
    public void MoveToTable(Guid diningTableId, string tableName)
    {
        if (!IsOpenTab)
            throw new DomainException("La cuenta ya está cerrada.");

        DiningTableId = diningTableId;
        OpenTableId = diningTableId;
        TableNumber = tableName.Trim();
    }

    /// <summary>Closes a paid tab and frees its table. The customer is not messaged: they are leaving.</summary>
    public void CloseTab(IOrderStateMachine stateMachine, string? performedBy)
    {
        if (!IsOpenTab)
            throw new DomainException("La cuenta ya está cerrada.");
        if (Balance > 0)
            throw new DomainException(
                $"La cuenta tiene un saldo de ${Balance.ToString("N2", CultureInfo.InvariantCulture)}: cóbralo antes de cerrarla.");

        Transition(OrderStatus.Delivered, stateMachine, performedBy, notifyCustomer: false);
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
            throw new DomainException("Solo las órdenes de servicio pueden tener detalle de servicio.");

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
    /// The status a new order starts in (confirmed or accepted by the business, or accepted automatically
    /// online). The customer hears about the order once, from its creation, and not again for this step.
    /// </summary>
    public void AdvanceOnCreation(OrderStatus newStatus, IOrderStateMachine stateMachine, string? performedBy)
        => Transition(newStatus, stateMachine, performedBy, notifyCustomer: false);

    /// <summary>
    /// Closes a fully paid counter sale: the customer leaves with what they bought, so nobody has to mark
    /// it delivered by hand. Food that goes through the kitchen closes only once it is ready, and a service
    /// only when none of its tracked pieces is still being worked on (those close when their pieces are
    /// completed). The customer is not messaged about it: they are at the counter.
    /// </summary>
    /// <returns>True when the order was closed.</returns>
    public bool CloseIfPaidCounterSale(IOrderStateMachine stateMachine, bool waitsForKitchen, string? performedBy)
    {
        // A tab can be paid round by round and keep ordering: it closes only when the table leaves (CloseTab).
        if (!CreatedByBusiness || IsTab || _items.Count == 0 || Balance > 0)
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
        ReleaseTableWhenClosed();
        _statusHistory.Add(new OrderStatusHistory(TenantId, Id, previousStatus, newStatus, performedBy));
        AddDomainEvent(new OrderStatusChangedEvent(Id, TenantId, previousStatus, newStatus, notifyCustomer));
    }

    private static bool IsClosedStatus(OrderStatus status)
        => status is OrderStatus.Delivered or OrderStatus.Cancelled or OrderStatus.Completed;

    private void ReleaseTableWhenClosed()
    {
        if (IsClosedStatus(Status))
            OpenTableId = null;
    }

    public void Cancel(IOrderStateMachine stateMachine, string? performedBy, string? reason = null)
    {
        if (!stateMachine.CanCancel(Status))
            throw new InvalidOrderStateTransitionException(Status, OrderStatus.Cancelled, BusinessType);

        // Cancelling with money still kept would leave payments and cash counted for an order that no
        // longer exists: the money is refunded first (RegisterRefund), and then the order is cancelled.
        if (NetPaidAmount > 0)
            throw new DomainException(
                "La orden tiene pagos sin reembolsar; reembólsalos antes de cancelarla.");

        var previousStatus = Status;
        Status = OrderStatus.Cancelled;
        ReleaseTableWhenClosed();
        CancellationReason = reason;
        _statusHistory.Add(new OrderStatusHistory(TenantId, Id, previousStatus, OrderStatus.Cancelled, performedBy));

        // Pieces still pending or in progress belong to an order that no longer exists: they leave the
        // service board and can no longer be completed (which would consume inventory).
        foreach (var piece in _servicePieces.Where(p => p.Status is ServiceOrderPieceStatus.Accepted or ServiceOrderPieceStatus.InProgress))
            piece.UpdateStatus(ServiceOrderPieceStatus.Cancelled);

        AddDomainEvent(new OrderCancelledEvent(Id, TenantId, previousStatus, reason));
    }

    /// <summary>A new order is built before its folio is taken (<see cref="AssignNumber"/>).</summary>
    public const string NumberNotAssigned = "";
    public const string TipAboveTotalMessage = "La propina no puede ser mayor que el total de la cuenta.";

    /// <summary>
    /// Gives a new order the folio its business assigned. Handlers take it once the checks without side effects
    /// passed, so a refused sale rarely uses one. A stored order keeps its folio.
    /// </summary>
    public void AssignNumber(string orderNumber)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
            throw new DomainException("El folio del pedido es obligatorio.");
        if (!string.IsNullOrEmpty(OrderNumber))
            throw new DomainException("El pedido ya tiene folio.");

        OrderNumber = orderNumber.Trim();
    }

    public void MarkAsCreated()
    {
        if (string.IsNullOrEmpty(OrderNumber))
            throw new DomainException("El pedido no tiene folio.");
        if (IsDeliveryPlatformOrder && PlatformDetail is null)
            throw new DomainException(PlatformOrderRules.MissingFolioMessage);

        AddDomainEvent(new OrderCreatedEvent(
            Id, TenantId, OrderNumber, BusinessType, Total, Status, Channel,
            PlatformDetail?.ExternalOrderNumber, PlatformDetail?.CollectedBy));
    }

    public void LinkToCustomerUser(Guid userId)
    {
        if (IsDeliveryPlatformOrder)
            throw new DomainException(PlatformOrderRules.NoCustomerMessage);

        CustomerUserId = userId;
        TemporaryCustomerId = null;
    }

    /// <summary>
    /// Registers a new order of a delivery platform: copies the platform's terms and the amounts calculated from them
    /// (commission, its IVA, withholdings and expected settlement) into the order's <see cref="PlatformDetail"/>. When
    /// the platform charged the customer, the order is paid then: a «Plataforma» payment for the total, stamped at
    /// <paramref name="registeredAt"/>, which never enters a drawer. When the business charges (the courier pays on
    /// pickup), it is charged later like any other order.
    /// </summary>
    /// <param name="setting">The platform's settings in the business, which must be active.</param>
    /// <param name="number">The folio of the platform's app, stored as the payment's reference too.</param>
    /// <param name="registeredAt">The server's time: the detail and the payment share it.</param>
    /// <param name="inventoryLocationId">Stock location of the branch, recorded on the payment like any other.</param>
    public PlatformOrderDetail RegisterPlatformOrder(
        DeliveryPlatformSetting setting,
        PlatformOrderNumber number,
        PlatformCollectedBy collectedBy,
        DateTimeOffset registeredAt,
        Guid? inventoryLocationId)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(number);
        if (!IsDeliveryPlatformOrder)
            throw new DomainException(PlatformOrderRules.OnlyPlatformChannelsMessage);
        if (PlatformDetail is not null)
            throw new DomainException(PlatformOrderRules.AlreadyRegisteredMessage);
        if (_items.Count == 0 || Total <= 0)
            throw new DomainException(PlatformOrderRules.NeedsAProductMessage);
        if (_payments.Count > 0)
            throw new DomainException(PlatformOrderRules.BeforeAnyPaymentMessage);
        if (Status == OrderStatus.Cancelled)
            throw new DomainException(PlatformOrderRules.CancelledMessage);
        if (setting.TenantId != TenantId || setting.Channel != Channel)
            throw new DomainException(PlatformOrderRules.SettingOfAnotherPlatformMessage);
        if (!setting.IsActive)
            throw new DomainException(PlatformOrderRules.NotActiveMessage(Channel));

        var terms = setting.Terms;
        var amounts = PlatformOrderCalculator.Calculate(terms, collectedBy, SubTotal, Total, GetTaxBreakdown());
        PlatformDetail = new PlatformOrderDetail(
            TenantId, Id, Channel, number, collectedBy, setting.Revision, terms, Total, amounts, registeredAt);

        if (collectedBy == PlatformCollectedBy.Platform)
        {
            // The only way a «Plataforma» payment is made: RegisterPayment refuses the method.
            _payments.Add(new OrderPayment(
                tenantId: TenantId,
                orderId: Id,
                amount: Total,
                method: OrderPaymentMethod.Platform,
                paidAt: registeredAt,
                inventoryLocationId: inventoryLocationId,
                evidenceUrl: null,
                reference: number.Value,
                notes: null,
                tip: 0,
                cashReceived: null));
            PaidAmount = Total;
        }

        return PlatformDetail;
    }

    public OrderPayment RegisterPayment(
        decimal amount,
        OrderPaymentMethod method,
        DateTimeOffset paidAt,
        Guid? inventoryLocationId,
        string? evidenceUrl,
        string? reference,
        string? notes,
        decimal tip = 0,
        decimal? cashReceived = null)
    {
        // What a delivery platform charged is registered with the order (RegisterPlatformOrder), and is all it gets.
        // These rules depend on the channel and the payments, always loaded, never on the platform detail.
        if (method == OrderPaymentMethod.Platform)
            throw new DomainException(OrderPaymentMethodRules.PlatformOnlyOnCreationMessage);
        if (_payments.Any(p => p.Method == OrderPaymentMethod.Platform))
            throw new DomainException(PlatformOrderRules.PlatformChargedMessage);
        if (tip > 0 && IsDeliveryPlatformOrder)
            throw new DomainException(PlatformOrderRules.NoTipMessage);

        var payment = new OrderPayment(
            tenantId: TenantId,
            orderId: Id,
            amount: amount,
            method: method,
            paidAt: paidAt,
            inventoryLocationId: inventoryLocationId,
            evidenceUrl: evidenceUrl,
            reference: reference,
            notes: notes,
            tip: tip,
            cashReceived: cashReceived);

        if (Status == OrderStatus.Cancelled)
            throw new DomainException("No se pueden registrar pagos en una orden cancelada.");

        var newPaidAmount = PaidAmount + payment.Amount;
        if (newPaidAmount > Total)
            throw new DomainException("El pago excede el total de la orden.");
        // The tip may come with a small last payment of a mixed charge, but more tip than bill is a typing mistake.
        if (TipTotal + payment.Tip > Total)
            throw new DomainException(TipAboveTotalMessage);

        _payments.Add(payment);
        PaidAmount = newPaidAmount;
        return payment;
    }

    /// <summary>
    /// Gives money back, optionally for returned units. The payments and lines it compensates are kept
    /// as they were; the refund records the reason, the method and who made it.
    /// </summary>
    /// <param name="returnedItems">Units returned per order line (may be empty for a price adjustment).</param>
    public OrderRefund RegisterRefund(
        decimal amount,
        OrderPaymentMethod method,
        string reason,
        DateTimeOffset refundedAt,
        string refundedBy,
        Guid? inventoryLocationId,
        string? reference,
        bool restockItems,
        IReadOnlyCollection<(Guid OrderItemId, decimal Quantity)> returnedItems)
    {
        if (Status == OrderStatus.Cancelled)
            throw new DomainException("La orden está cancelada: no tiene pagos que reembolsar.");
        if (amount > NetPaidAmount)
            throw new DomainException("El reembolso no puede ser mayor a lo que se pagó y no se ha reembolsado.");
        // What a platform charged goes back as «Plataforma», outside every drawer, and only that does.
        var platformCharged = _payments.Any(p => p.Method == OrderPaymentMethod.Platform);
        if (method == OrderPaymentMethod.Platform && !platformCharged)
            throw new DomainException(PlatformOrderRules.PlatformRefundOnlyOfPlatformMessage);
        if (method != OrderPaymentMethod.Platform && platformCharged)
            throw new DomainException(PlatformOrderRules.PlatformChargedRefundMessage);

        var refund = new OrderRefund(
            TenantId, Id, amount, method, reason, refundedAt, refundedBy, inventoryLocationId, reference, restockItems);

        foreach (var (orderItemId, quantity) in returnedItems)
        {
            var item = _items.FirstOrDefault(x => x.Id == orderItemId)
                ?? throw new DomainException("El artículo devuelto no pertenece a esta orden.");
            item.RegisterReturn(quantity);
            refund.AddLine(item, quantity);
        }

        _refunds.Add(refund);
        RefundedAmount += amount;
        AddDomainEvent(new OrderRefundedEvent(Id, TenantId, refund.Id, amount, method, refund.Reason));
        return refund;
    }

    /// <summary>
    /// IVA by rate, computed from what the items charge and the policy the order was sold with. A line whose units were
    /// all cancelled charges nothing and is left out.
    /// </summary>
    public IReadOnlyList<OrderTaxLine> GetTaxBreakdown() => Breakdown(item => item.ChargedPrice);

    private IReadOnlyList<OrderTaxLine> Breakdown(Func<OrderItem, decimal> chargedPrice)
        => OrderTaxCalculator.Calculate(
            PricesIncludeTax,
            _items
                .Where(item => item.CancelledQuantity < item.Quantity)
                .Select(item => new TaxableAmount(chargedPrice(item), item.VatRate)));

    private (decimal SubTotal, decimal Tax) ComputeTotals(Func<OrderItem, decimal> chargedPrice)
    {
        var breakdown = Breakdown(chargedPrice);
        return (breakdown.Sum(line => line.TaxableBase), breakdown.Sum(line => line.TaxAmount));
    }

    public const string TotalBelowPaidMessage = "El total no puede quedar por debajo de lo cobrado.";

    /// <summary>
    /// Totals from the items. What was charged is never trimmed to fit a lower total: payments and drawers would no
    /// longer add up. No path lowers a charged total today (a dish already paid is refunded instead of cancelled).
    /// </summary>
    private void RecalculateTotals()
    {
        var (subTotal, tax) = ComputeTotals(item => item.ChargedPrice);
        EnsureCoversWhatWasCharged(subTotal + tax, PaidAmount);
        SubTotal = subTotal;
        Tax = tax;
        Total = subTotal + tax;
    }

    internal static void EnsureCoversWhatWasCharged(decimal total, decimal paidAmount)
    {
        if (paidAmount > total)
            throw new DomainException(TotalBelowPaidMessage);
    }
}
