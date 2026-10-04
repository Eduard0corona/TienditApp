using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
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
    private readonly ICurrentUserService? _currentUserService;

    public AddOrderPaymentCommandHandler(
        IApplicationDbContext context,
        IDateTimeProvider dateTimeProvider,
        IMapper mapper,
        IOrderCashService? orderCashService = null,
        ICurrentUserService? currentUserService = null)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
        _mapper = mapper;
        _orderCashService = orderCashService ?? NoOpOrderCashService.Instance;
        _currentUserService = currentUserService;
    }

    public async Task<Result<OrderDto>> Handle(AddOrderPaymentCommand request, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new EntityNotFoundException("Order", request.OrderId);

        var registrar = new OrderPaymentRegistrar(_context, _orderCashService, _dateTimeProvider);
        var payment = await registrar.RegisterAsync(
            order,
            new OrderPaymentInput(
                request.Amount,
                request.Method,
                request.InventoryLocationId,
                request.EvidenceUrl,
                request.Reference,
                request.Notes),
            cancellationToken);
        if (payment.IsFailure)
            return Result.Failure<OrderDto>(payment.Error!);

        // The payment that settles a counter sale also closes it (when the business keeps that policy).
        var salesSettings = await TenantSalesSettings.ReadAsync(_context, order.TenantId, cancellationToken);
        var capabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(_context, order.TenantId, cancellationToken);
        CounterSaleClosing.TryClose(order, salesSettings, capabilities, _currentUserService?.UserName);

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
