using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Commands;

/// <summary>
/// An order to the customer's address goes out with the courier (Fase 22D): the trip asked for in Paquetenvia, whether the
/// courier collects the order in cash and the bill the customer pays with. Corrected while it is on its way. Whoever takes
/// the orders.
/// </summary>
/// <param name="CollectsCash">The courier collects the balance from the customer, in cash.</param>
/// <param name="PaysWith">The bill the customer pays with, for the change; only when the courier collects.</param>
[RequiresPermission(TenantPermission.ManageOrders)]
public record DispatchOrderCommand(
    Guid OrderId,
    DeliveryCourier Courier,
    string Folio,
    string? TrackingUrl,
    bool CollectsCash,
    decimal? PaysWith) : IRequest<Result>;

public class DispatchOrderCommandValidator : AbstractValidator<DispatchOrderCommand>
{
    public DispatchOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty().WithMessage("Recarga e intenta de nuevo.");
        RuleFor(x => x.Courier).IsInEnum().WithMessage(CourierShipment.CourierMessage);
        RuleFor(x => x.Folio).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(CourierShipment.FolioRequiredMessage)
            .MaximumLength(PlatformOrderNumber.MaxInputLength).WithMessage(CourierShipment.FolioInvalidMessage);
        RuleFor(x => x.TrackingUrl)
            .MaximumLength(CourierShipment.MaxTrackingUrlLength + 20).WithMessage(CourierShipment.TrackingUrlMessage);
        RuleFor(x => x.PaysWith)
            .Must(paysWith => paysWith is null || HomeDeliveryRules.IsPaysWith(paysWith.Value))
            .WithMessage(HomeDeliveryRules.PaysWithMessage);
    }
}

public class DispatchOrderCommandHandler : IRequestHandler<DispatchOrderCommand, Result>
{
    public const string OrderDispatched = "OrderDispatched";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _clock;

    public DispatchOrderCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
        _clock = clock;
    }

    public async Task<Result> Handle(DispatchOrderCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == request.OrderId && o.TenantId == tenantId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Order), request.OrderId);

        var wentOutBefore = order.Delivery?.IsDispatched == true;
        try
        {
            var shipment = CourierShipment.From(request.Courier, request.Folio, request.TrackingUrl);
            if (!order.Dispatch(shipment, request.CollectsCash, request.PaysWith, _clock.UtcNow, _currentUserService.UserName))
                return Result.Success();

            // The courier, its folio and what it collects; whether there is a link, never the link nor the address.
            _context.AuditLogs.Add(new AuditLog(
                tenantId: tenantId,
                entityName: nameof(Order),
                entityId: order.Id.ToString(),
                action: OrderDispatched,
                performedBy: _currentUserService.UserName ?? "system",
                metadata: JsonSerializer.Serialize(new
                {
                    order.OrderNumber,
                    Courier = shipment.Courier.ToString(),
                    shipment.Folio,
                    HasTrackingUrl = shipment.TrackingUrl is not null,
                    order.Delivery!.CashToCollect,
                    order.Delivery.PaysWith,
                    Correction = wentOutBefore
                })));
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
