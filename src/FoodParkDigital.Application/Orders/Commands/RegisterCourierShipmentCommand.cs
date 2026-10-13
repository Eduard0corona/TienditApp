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
/// Writes down (or corrects) the courier's trip of an order that goes to the customer's address (Fase 22C): today the
/// folio of the trip asked for in Paquetenvia and, optionally, the link that follows it. Whoever takes the orders.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record RegisterCourierShipmentCommand(Guid OrderId, DeliveryCourier Courier, string Folio, string? TrackingUrl)
    : IRequest<Result>;

public class RegisterCourierShipmentCommandValidator : AbstractValidator<RegisterCourierShipmentCommand>
{
    public RegisterCourierShipmentCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty().WithMessage("Recarga e intenta de nuevo.");
        RuleFor(x => x.Courier).IsInEnum().WithMessage(CourierShipment.CourierMessage);
        RuleFor(x => x.Folio).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(CourierShipment.FolioRequiredMessage)
            .MaximumLength(PlatformOrderNumber.MaxInputLength).WithMessage(CourierShipment.FolioInvalidMessage);
        RuleFor(x => x.TrackingUrl)
            .MaximumLength(CourierShipment.MaxTrackingUrlLength + 20).WithMessage(CourierShipment.TrackingUrlMessage);
    }
}

public class RegisterCourierShipmentCommandHandler : IRequestHandler<RegisterCourierShipmentCommand, Result>
{
    public const string ShipmentRegistered = "OrderCourierShipmentRegistered";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _clock;

    public RegisterCourierShipmentCommandHandler(
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

    public async Task<Result> Handle(RegisterCourierShipmentCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == request.OrderId && o.TenantId == tenantId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Order), request.OrderId);

        var previousFolio = order.Delivery?.CourierFolio;
        try
        {
            var shipment = CourierShipment.From(request.Courier, request.Folio, request.TrackingUrl);
            if (!order.RegisterCourierShipment(shipment, _clock.UtcNow, _currentUserService.UserName))
                return Result.Success();

            // The courier and its folio, before and after; whether there is a link, never the link, which can show where
            // the order goes.
            _context.AuditLogs.Add(new AuditLog(
                tenantId: tenantId,
                entityName: nameof(Order),
                entityId: order.Id.ToString(),
                action: ShipmentRegistered,
                performedBy: _currentUserService.UserName ?? "system",
                metadata: JsonSerializer.Serialize(new
                {
                    order.OrderNumber,
                    Courier = shipment.Courier.ToString(),
                    PreviousFolio = previousFolio,
                    shipment.Folio,
                    HasTrackingUrl = shipment.TrackingUrl is not null
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
