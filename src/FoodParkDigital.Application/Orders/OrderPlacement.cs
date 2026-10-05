using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;

namespace FoodParkDigital.Application.Orders;

/// <summary>
/// The steps every channel follows to place an order (the counter, the chat, the route of a business and the online
/// menu): the service it receives and its pieces, and the status the team starts it with.
/// </summary>
internal static class OrderPlacement
{
    /// <summary>
    /// When the service was received, promised, its intake notes and its photos (service orders only). The photos must
    /// be ones the business uploaded through the app: they are private and only open for its members.
    /// </summary>
    /// <returns>Why the service detail is refused, or null once it is applied.</returns>
    public static string? ApplyServiceDetail(Order order, ServiceOrderDetailInput? detail, DateTimeOffset now)
    {
        if (order.OrderType != OrderType.Service || detail is null)
            return null;

        var beforeError = OrderUploadRules.NormalizeServicePhotos(detail.BeforePhotosJson, order.TenantId, out var beforePhotos);
        var afterError = OrderUploadRules.NormalizeServicePhotos(detail.AfterPhotosJson, order.TenantId, out var afterPhotos);
        if ((beforeError ?? afterError) is { } photosError)
            return photosError;

        order.SetServiceDetail(
            receivedAt: detail.ReceivedAt ?? now,
            promisedAt: detail.PromisedAt,
            priority: detail.Priority,
            intakeNotes: detail.IntakeNotes?.Trim(),
            deliveryNotes: detail.DeliveryNotes?.Trim(),
            intakeChecklistJson: detail.IntakeChecklistJson,
            beforePhotosJson: beforePhotos,
            afterPhotosJson: afterPhotos);
        return null;
    }

    /// <summary>
    /// The pieces the service tracks: the ones described at the counter, or one per unit of each trackable service.
    /// </summary>
    /// <returns>Why the pieces are refused, or null once they are added.</returns>
    public static string? AddServicePieces(
        Order order,
        ServiceOrderDetailInput? detail,
        IReadOnlyList<(Product Product, CreateOrderItemDto Item)> trackableServices,
        IReadOnlySet<Guid> orderedProducts)
    {
        var describedPieces = detail?.Pieces
            .Where(piece => !string.IsNullOrWhiteSpace(piece.ItemName))
            .ToList()
            ?? [];

        var error = OrderLimits.ValidateServicePieces(describedPieces, trackableServices.ToList(), orderedProducts);
        if (error is not null)
            return error;

        if (describedPieces.Count > 0)
        {
            foreach (var piece in describedPieces)
            {
                order.AddServicePiece(
                    itemName: piece.ItemName.Trim(),
                    brand: piece.Brand?.Trim(),
                    model: piece.Model?.Trim(),
                    color: piece.Color?.Trim(),
                    size: piece.Size?.Trim(),
                    material: piece.Material?.Trim(),
                    conditionNotes: piece.ConditionNotes?.Trim(),
                    requestedService: piece.RequestedService?.Trim(),
                    internalNotes: piece.InternalNotes?.Trim(),
                    serviceProductId: piece.ServiceProductId,
                    isTrackable: piece.IsTrackable,
                    notifyCustomer: piece.NotifyCustomer);
            }

            return null;
        }

        foreach (var (product, item) in trackableServices)
        {
            var units = (int)item.Quantity;
            for (var index = 0; index < units; index++)
            {
                order.AddServicePiece(
                    itemName: item.Quantity > 1 ? $"{product.Name} #{index + 1}" : product.Name,
                    brand: null,
                    model: null,
                    color: null,
                    size: null,
                    material: null,
                    conditionNotes: item.Notes?.Trim(),
                    requestedService: product.Name,
                    internalNotes: null,
                    serviceProductId: product.Id,
                    isTrackable: true,
                    notifyCustomer: product.NotifyCustomer);
            }
        }

        return null;
    }

    /// <summary>What the team captures starts confirmed (a service, accepted) when its state machine allows it.</summary>
    public static void ApplyBusinessDefaultStatus(Order order, string? performedBy)
    {
        var stateMachine = OrderStateMachineFactory.Create(order.BusinessType, order.OrderType);
        var defaultStatus = order.OrderType == OrderType.Service ? OrderStatus.Accepted : OrderStatus.Confirmed;

        if (stateMachine.GetAllowedTransitions(order.Status).Contains(defaultStatus))
            order.AdvanceOnCreation(defaultStatus, stateMachine, performedBy);
    }
}
