using System.Linq.Expressions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Application.Orders.DTOs;

/// <summary>
/// Orders as the API returns them. <see cref="ToDto(Order)"/> maps a loaded order; <see cref="AsDto"/> is the same
/// mapping written for SQL (the orders list), so <c>OrderMappingTests</c> keeps both equal. What the DTO derives from
/// the rules (allowed actions, IVA breakdown) is filled by <see cref="OrderDtoStateExtensions.ApplyStateMetadata(OrderDto)"/>.
/// </summary>
public static class OrderMappings
{
    public static OrderDto ToDto(this Order order) => new()
    {
        Id = order.Id,
        CustomerUserId = order.CustomerUserId,
        TemporaryCustomerId = order.TemporaryCustomerId,
        CreatedByBusiness = order.CreatedByBusiness,
        OrderNumber = order.OrderNumber,
        BusinessType = order.BusinessType,
        OrderType = order.OrderType,
        Status = order.Status,
        CustomerName = order.CustomerName,
        CustomerPhone = order.CustomerPhone,
        CustomerNotes = order.CustomerNotes,
        TableNumber = order.TableNumber,
        PricesIncludeTax = order.PricesIncludeTax,
        Channel = order.Channel,
        PlatformOrder = order.PlatformDetail is null ? null : new OrderPlatformDto
        {
            ExternalOrderNumber = order.PlatformDetail.ExternalOrderNumber,
            CollectedBy = order.PlatformDetail.CollectedBy
        },
        Delivery = order.Delivery is null ? null : new OrderDeliveryDto
        {
            PostalCode = order.Delivery.PostalCode,
            AddressLine = order.Delivery.AddressLine,
            References = order.Delivery.References,
            ZoneName = order.Delivery.ZoneName,
            Fee = order.Delivery.Fee,
            FeeTaxRate = order.Delivery.FeeTaxRate,
            Courier = order.Delivery.Courier,
            CourierFolio = order.Delivery.CourierFolio,
            TrackingUrl = order.Delivery.TrackingUrl,
            ShipmentRegisteredAt = order.Delivery.ShipmentRegisteredAt,
            ShipmentRegisteredBy = order.Delivery.ShipmentRegisteredBy,
            PaysWith = order.Delivery.PaysWith,
            DispatchedAt = order.Delivery.DispatchedAt,
            DispatchedBy = order.Delivery.DispatchedBy,
            CashToCollect = order.Delivery.CashToCollect,
            AwaitsCourierCash = order.AwaitsCourierCash
        },
        VisitCharge = order.VisitCharge is null ? null : new OrderVisitChargeDto
        {
            AppointmentId = order.VisitCharge.AppointmentId,
            ZoneName = order.VisitCharge.ZoneName,
            Fee = order.VisitCharge.Fee,
            FeeTaxRate = order.VisitCharge.FeeTaxRate
        },
        BranchId = order.BranchId,
        CapturedOfflineAt = order.CapturedOfflineAt,
        SubTotal = order.SubTotal,
        Tax = order.Tax,
        Total = order.Total,
        PaidAmount = order.PaidAmount,
        Balance = order.Balance,
        TipTotal = order.TipTotal,
        RefundedAmount = order.RefundedAmount,
        NetPaidAmount = order.NetPaidAmount,
        CancellationReason = order.CancellationReason,
        CreatedAt = order.CreatedAt,
        UpdatedAt = order.UpdatedAt,
        Items = order.Items.Select(ToDto).ToList(),
        StatusHistory = order.StatusHistory.Select(ToDto).ToList(),
        ServiceDetail = order.ServiceDetail?.ToDto(),
        ServicePieces = order.ServicePieces.Select(ToDto).ToList(),
        Payments = order.Payments.Select(ToDto).ToList(),
        Refunds = order.Refunds.Select(ToDto).ToList(),
        ItemCancellations = order.ItemCancellations.OrderBy(c => c.CancelledAt).Select(ToDto).ToList(),
        IsTab = order.IsTab,
        IsOpenTab = order.IsOpenTab,
        DiningTableId = order.DiningTableId,
        KitchenTickets = order.KitchenTickets.OrderBy(t => t.Round).Select(ToSummaryDto).ToList()
    };

    /// <summary>The order on the kitchen board; the folio of a delivery platform needs its detail loaded.</summary>
    public static KitchenOrderDto ToKitchenDto(this Order order) => new()
    {
        Id = order.Id,
        OrderNumber = order.OrderNumber,
        Channel = order.Channel,
        ExternalOrderNumber = order.PlatformDetail?.ExternalOrderNumber,
        IsDelivery = order.Delivery != null,
        Status = order.Status,
        CustomerName = order.CustomerName,
        TableNumber = order.TableNumber,
        CustomerNotes = order.CustomerNotes,
        CreatedAt = order.CreatedAt,
        Items = order.Items.Select(ToDto).ToList()
    };

    public static OrderItemDto ToDto(this OrderItem item) => new()
    {
        Id = item.Id,
        ProductId = item.ProductId,
        ProductName = item.ProductName,
        Quantity = item.Quantity,
        ReturnedQuantity = item.ReturnedQuantity,
        CancelledQuantity = item.CancelledQuantity,
        UnitPrice = item.UnitPrice,
        TotalPrice = item.TotalPrice,
        ChargedPrice = item.ChargedPrice,
        Notes = item.Notes,
        TaxRate = item.TaxRate,
        IsTaxExempt = item.IsTaxExempt,
        Unit = item.Unit,
        SatProductCode = item.SatProductCode,
        SatUnitCode = item.SatUnitCode,
        TaxObject = item.TaxObject,
        Round = item.Round,
        Options = item.Options
            .OrderBy(o => o.GroupName)
            .ThenBy(o => o.OptionName)
            .Select(o => new OrderItemOptionDto
            {
                OptionId = o.OptionId,
                GroupName = o.GroupName,
                OptionName = o.OptionName,
                PriceDelta = o.PriceDelta
            })
            .ToList()
    };

    public static OrderStatusHistoryDto ToDto(this OrderStatusHistory history) => new()
    {
        Id = history.Id,
        PreviousStatus = history.PreviousStatus,
        NewStatus = history.NewStatus,
        ChangedAt = history.ChangedAt,
        ChangedBy = history.ChangedBy
    };

    public static ServiceOrderDetailDto ToDto(this ServiceOrderDetail detail) => new()
    {
        Id = detail.Id,
        ReceivedAt = detail.ReceivedAt,
        PromisedAt = detail.PromisedAt,
        Priority = detail.Priority,
        IntakeNotes = detail.IntakeNotes,
        DeliveryNotes = detail.DeliveryNotes,
        IntakeChecklistJson = detail.IntakeChecklistJson,
        BeforePhotosJson = detail.BeforePhotosJson,
        AfterPhotosJson = detail.AfterPhotosJson
    };

    public static ServiceOrderPieceDto ToDto(this ServiceOrderPiece piece) => new()
    {
        Id = piece.Id,
        ItemName = piece.ItemName,
        ServiceProductId = piece.ServiceProductId,
        IsTrackable = piece.IsTrackable,
        NotifyCustomer = piece.NotifyCustomer,
        AssignedToUserId = piece.AssignedToUserId,
        AssignedToName = piece.AssignedToName,
        AssignedAt = piece.AssignedAt,
        Brand = piece.Brand,
        Model = piece.Model,
        Color = piece.Color,
        Size = piece.Size,
        Material = piece.Material,
        ConditionNotes = piece.ConditionNotes,
        RequestedService = piece.RequestedService,
        InternalNotes = piece.InternalNotes,
        Status = piece.Status
    };

    public static OrderPaymentDto ToDto(this OrderPayment payment) => new()
    {
        Id = payment.Id,
        Amount = payment.Amount,
        Method = payment.Method,
        PaidAt = payment.PaidAt,
        InventoryLocationId = payment.InventoryLocationId,
        EvidenceUrl = payment.EvidenceUrl,
        Reference = payment.Reference,
        Notes = payment.Notes,
        Tip = payment.Tip,
        CashReceived = payment.CashReceived,
        Change = payment.Change
    };

    public static OrderRefundDto ToDto(this OrderRefund refund) => new()
    {
        Id = refund.Id,
        Amount = refund.Amount,
        Method = refund.Method,
        Reason = refund.Reason,
        RefundedAt = refund.RefundedAt,
        RefundedBy = refund.RefundedBy,
        Reference = refund.Reference,
        RestockItems = refund.RestockItems,
        Lines = refund.Lines.Select(l => new OrderRefundLineDto { OrderItemId = l.OrderItemId, Quantity = l.Quantity }).ToList()
    };

    public static OrderItemCancellationDto ToDto(this OrderItemCancellation cancellation) => new()
    {
        Id = cancellation.Id,
        OrderItemId = cancellation.OrderItemId,
        Round = cancellation.Round,
        Quantity = cancellation.Quantity,
        Amount = cancellation.Amount,
        Reason = cancellation.Reason,
        ReturnedToStock = cancellation.ReturnedToStock,
        CancelledAt = cancellation.CancelledAt,
        CancelledBy = cancellation.CancelledBy
    };

    public static KitchenTicketSummaryDto ToSummaryDto(this KitchenTicket ticket) => new()
    {
        Id = ticket.Id,
        Round = ticket.Round,
        Status = ticket.Status,
        SentAt = ticket.SentAt,
        SentBy = ticket.SentBy,
        ReadyAt = ticket.ReadyAt,
        ServedAt = ticket.ServedAt
    };

    /// <summary>
    /// <see cref="ToDto(Order)"/> for SQL: the computed members of the order (balance, tips, open tab) are written as
    /// columns so the database computes them.
    /// </summary>
    public static readonly Expression<Func<Order, OrderDto>> AsDto = order => new OrderDto
    {
        Id = order.Id,
        CustomerUserId = order.CustomerUserId,
        TemporaryCustomerId = order.TemporaryCustomerId,
        CreatedByBusiness = order.CreatedByBusiness,
        OrderNumber = order.OrderNumber,
        BusinessType = order.BusinessType,
        OrderType = order.OrderType,
        Status = order.Status,
        CustomerName = order.CustomerName,
        CustomerPhone = order.CustomerPhone,
        CustomerNotes = order.CustomerNotes,
        TableNumber = order.TableNumber,
        PricesIncludeTax = order.PricesIncludeTax,
        Channel = order.Channel,
        PlatformOrder = order.PlatformDetail == null ? null : new OrderPlatformDto
        {
            ExternalOrderNumber = order.PlatformDetail.ExternalOrderNumber,
            CollectedBy = order.PlatformDetail.CollectedBy
        },
        Delivery = order.Delivery == null ? null : new OrderDeliveryDto
        {
            PostalCode = order.Delivery.PostalCode,
            AddressLine = order.Delivery.AddressLine,
            References = order.Delivery.References,
            ZoneName = order.Delivery.ZoneName,
            Fee = order.Delivery.Fee,
            FeeTaxRate = order.Delivery.FeeTaxRate,
            Courier = order.Delivery.Courier,
            CourierFolio = order.Delivery.CourierFolio,
            TrackingUrl = order.Delivery.TrackingUrl,
            ShipmentRegisteredAt = order.Delivery.ShipmentRegisteredAt,
            ShipmentRegisteredBy = order.Delivery.ShipmentRegisteredBy,
            PaysWith = order.Delivery.PaysWith,
            DispatchedAt = order.Delivery.DispatchedAt,
            DispatchedBy = order.Delivery.DispatchedBy,
            CashToCollect = order.Delivery.CashToCollect,
            // Order.AwaitsCourierCash, written out so the database reads it.
            AwaitsCourierCash = order.Delivery.CashToCollect != null && order.Status == OrderStatus.Delivered
                && order.Total > order.PaidAmount
        },
        VisitCharge = order.VisitCharge == null ? null : new OrderVisitChargeDto
        {
            AppointmentId = order.VisitCharge.AppointmentId,
            ZoneName = order.VisitCharge.ZoneName,
            Fee = order.VisitCharge.Fee,
            FeeTaxRate = order.VisitCharge.FeeTaxRate
        },
        BranchId = order.BranchId,
        CapturedOfflineAt = order.CapturedOfflineAt,
        SubTotal = order.SubTotal,
        Tax = order.Tax,
        Total = order.Total,
        PaidAmount = order.PaidAmount,
        Balance = order.Total - order.PaidAmount,
        TipTotal = order.Payments.Sum(p => p.Tip),
        RefundedAmount = order.RefundedAmount,
        NetPaidAmount = order.PaidAmount - order.RefundedAmount,
        CancellationReason = order.CancellationReason,
        CreatedAt = order.CreatedAt,
        UpdatedAt = order.UpdatedAt,
        Items = order.Items.Select(item => new OrderItemDto
        {
            Id = item.Id,
            ProductId = item.ProductId,
            ProductName = item.ProductName,
            Quantity = item.Quantity,
            ReturnedQuantity = item.ReturnedQuantity,
            CancelledQuantity = item.CancelledQuantity,
            UnitPrice = item.UnitPrice,
            TotalPrice = item.TotalPrice,
            // Same as OrderItem.ChargedPrice.
            ChargedPrice = item.TotalPrice - item.CancelledAmount,
            Notes = item.Notes,
            TaxRate = item.TaxRate,
            IsTaxExempt = item.IsTaxExempt,
            Unit = item.Unit,
            SatProductCode = item.SatProductCode,
            SatUnitCode = item.SatUnitCode,
            TaxObject = item.TaxObject,
            Round = item.Round,
            Options = item.Options
                .OrderBy(o => o.GroupName)
                .ThenBy(o => o.OptionName)
                .Select(o => new OrderItemOptionDto
                {
                    OptionId = o.OptionId,
                    GroupName = o.GroupName,
                    OptionName = o.OptionName,
                    PriceDelta = o.PriceDelta
                })
                .ToList()
        }).ToList(),
        StatusHistory = order.StatusHistory.Select(history => new OrderStatusHistoryDto
        {
            Id = history.Id,
            PreviousStatus = history.PreviousStatus,
            NewStatus = history.NewStatus,
            ChangedAt = history.ChangedAt,
            ChangedBy = history.ChangedBy
        }).ToList(),
        ServiceDetail = order.ServiceDetail == null ? null : new ServiceOrderDetailDto
        {
            Id = order.ServiceDetail.Id,
            ReceivedAt = order.ServiceDetail.ReceivedAt,
            PromisedAt = order.ServiceDetail.PromisedAt,
            Priority = order.ServiceDetail.Priority,
            IntakeNotes = order.ServiceDetail.IntakeNotes,
            DeliveryNotes = order.ServiceDetail.DeliveryNotes,
            IntakeChecklistJson = order.ServiceDetail.IntakeChecklistJson,
            BeforePhotosJson = order.ServiceDetail.BeforePhotosJson,
            AfterPhotosJson = order.ServiceDetail.AfterPhotosJson
        },
        ServicePieces = order.ServicePieces.Select(piece => new ServiceOrderPieceDto
        {
            Id = piece.Id,
            ItemName = piece.ItemName,
            ServiceProductId = piece.ServiceProductId,
            IsTrackable = piece.IsTrackable,
            NotifyCustomer = piece.NotifyCustomer,
            AssignedToUserId = piece.AssignedToUserId,
            AssignedToName = piece.AssignedToName,
            AssignedAt = piece.AssignedAt,
            Brand = piece.Brand,
            Model = piece.Model,
            Color = piece.Color,
            Size = piece.Size,
            Material = piece.Material,
            ConditionNotes = piece.ConditionNotes,
            RequestedService = piece.RequestedService,
            InternalNotes = piece.InternalNotes,
            Status = piece.Status
        }).ToList(),
        Payments = order.Payments.Select(payment => new OrderPaymentDto
        {
            Id = payment.Id,
            Amount = payment.Amount,
            Method = payment.Method,
            PaidAt = payment.PaidAt,
            InventoryLocationId = payment.InventoryLocationId,
            EvidenceUrl = payment.EvidenceUrl,
            Reference = payment.Reference,
            Notes = payment.Notes,
            Tip = payment.Tip,
            CashReceived = payment.CashReceived,
            Change = payment.CashReceived == null ? 0 : payment.CashReceived.Value - payment.Amount - payment.Tip
        }).ToList(),
        Refunds = order.Refunds.Select(refund => new OrderRefundDto
        {
            Id = refund.Id,
            Amount = refund.Amount,
            Method = refund.Method,
            Reason = refund.Reason,
            RefundedAt = refund.RefundedAt,
            RefundedBy = refund.RefundedBy,
            Reference = refund.Reference,
            RestockItems = refund.RestockItems,
            Lines = refund.Lines.Select(l => new OrderRefundLineDto { OrderItemId = l.OrderItemId, Quantity = l.Quantity }).ToList()
        }).ToList(),
        ItemCancellations = order.ItemCancellations.OrderBy(c => c.CancelledAt).Select(cancellation => new OrderItemCancellationDto
        {
            Id = cancellation.Id,
            OrderItemId = cancellation.OrderItemId,
            Round = cancellation.Round,
            Quantity = cancellation.Quantity,
            Amount = cancellation.Amount,
            Reason = cancellation.Reason,
            ReturnedToStock = cancellation.ReturnedToStock,
            CancelledAt = cancellation.CancelledAt,
            CancelledBy = cancellation.CancelledBy
        }).ToList(),
        IsTab = order.IsTab,
        // Same rule as Order.IsOpenTab.
        IsOpenTab = order.IsTab
            && order.Status != OrderStatus.Delivered
            && order.Status != OrderStatus.Cancelled
            && order.Status != OrderStatus.Completed,
        DiningTableId = order.DiningTableId,
        KitchenTickets = order.KitchenTickets.OrderBy(t => t.Round).Select(ticket => new KitchenTicketSummaryDto
        {
            Id = ticket.Id,
            Round = ticket.Round,
            Status = ticket.Status,
            SentAt = ticket.SentAt,
            SentBy = ticket.SentBy,
            ReadyAt = ticket.ReadyAt,
            ServedAt = ticket.ServedAt
        }).ToList()
    };
}
