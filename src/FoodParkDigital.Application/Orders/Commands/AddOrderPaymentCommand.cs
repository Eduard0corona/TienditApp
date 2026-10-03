using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Commands;

public record AddOrderPaymentCommand : IRequest<Result<OrderDto>>
{
    public Guid OrderId { get; init; }
    public decimal Amount { get; init; }
    public OrderPaymentMethod Method { get; init; }
    public Guid? InventoryLocationId { get; init; }
    public string? EvidenceUrl { get; init; }
    public string? Reference { get; init; }
    public string? Notes { get; init; }
}

public class AddOrderPaymentCommandValidator : AbstractValidator<AddOrderPaymentCommand>
{
    public AddOrderPaymentCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        // Payments are stored with 2 decimals; more precision would make orders and the cash ledger diverge.
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.EvidenceUrl).MaximumLength(1000);
        RuleFor(x => x.Reference).MaximumLength(120);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class AddOrderPaymentCommandHandler : IRequestHandler<AddOrderPaymentCommand, Result<OrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IMapper _mapper;
    private readonly IOrderCashService _orderCashService;

    public AddOrderPaymentCommandHandler(
        IApplicationDbContext context,
        IDateTimeProvider dateTimeProvider,
        IMapper mapper,
        IOrderCashService? orderCashService = null)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
        _mapper = mapper;
        _orderCashService = orderCashService ?? NoOpOrderCashService.Instance;
    }

    public async Task<Result<OrderDto>> Handle(AddOrderPaymentCommand request, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new EntityNotFoundException("Order", request.OrderId);

        if (order.Status == OrderStatus.Cancelled)
            return Result.Failure<OrderDto>("No se pueden registrar pagos en una orden cancelada.");

        // The evidence is shown to staff as a link: it must be a file uploaded for this tenant.
        if (!OrderUploadRules.IsOwnPaymentEvidence(request.EvidenceUrl, order.TenantId))
            return Result.Failure<OrderDto>("La evidencia del pago debe subirse desde el sistema.");

        Guid? inventoryLocationId = request.InventoryLocationId;
        if (inventoryLocationId.HasValue)
        {
            var exists = await _context.InventoryLocations
                .AnyAsync(
                    x => x.Id == inventoryLocationId.Value
                      && x.TenantId == order.TenantId
                      && x.IsActive,
                    cancellationToken);

            if (!exists)
                return Result.Failure<OrderDto>("La sucursal seleccionada no es valida.");
        }
        else
        {
            inventoryLocationId = await _context.InventoryLocations
                .Where(x => x.TenantId == order.TenantId && x.IsActive && x.IsDefault)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var payment = order.RegisterPayment(
            amount: request.Amount,
            method: request.Method,
            // Stamped by the server: a client-provided date could rewrite already reported cuts.
            paidAt: _dateTimeProvider.UtcNow,
            inventoryLocationId: inventoryLocationId,
            evidenceUrl: request.EvidenceUrl?.Trim(),
            reference: request.Reference?.Trim(),
            notes: request.Notes?.Trim());
        await _orderCashService.RegisterOrderPaymentAsync(order, payment, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        var updatedOrder = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .Include(o => o.ServiceDetail)
            .Include(o => o.ServicePieces)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new EntityNotFoundException("Order", request.OrderId);

        var dto = _mapper.Map<OrderDto>(updatedOrder);
        dto.ApplyStateMetadata();
        return Result.Success(dto);
    }
}
