using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Commands;

[RequiresPermission(TenantPermission.ChargeOrders)]
public record AddOrderPaymentCommand : IRequest<Result<OrderDto>>
{
    public Guid OrderId { get; init; }
    public decimal Amount { get; init; }
    public OrderPaymentMethod Method { get; init; }
    public Guid? InventoryLocationId { get; init; }
    public string? EvidenceUrl { get; init; }
    public string? Reference { get; init; }
    public string? Notes { get; init; }
    /// <summary>On top of the amount, for the team: not part of the sale or its balance. 0 when not sent.</summary>
    [System.ComponentModel.DefaultValue(0)]
    public decimal Tip { get; init; }
    /// <summary>What the customer handed over in cash, to keep the change with the payment.</summary>
    public decimal? CashReceived { get; init; }
}

public class AddOrderPaymentCommandValidator : AbstractValidator<AddOrderPaymentCommand>
{
    public AddOrderPaymentCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        // Payments are stored with 2 decimals; more precision would make orders and the cash ledger diverge.
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
        RuleFor(x => x.Method).IsInEnum();
        // What a delivery platform charged is recorded by its order when it is created, for the total.
        RuleFor(x => x.Method).NotEqual(OrderPaymentMethod.Platform)
            .WithMessage(OrderPaymentMethodRules.PlatformOnlyOnCreationMessage);
        // What the customer paid online is recorded when the payment places the order (Fase 24I).
        RuleFor(x => x.Method).NotEqual(OrderPaymentMethod.Online)
            .WithMessage(OrderPaymentMethodRules.OnlineOnlyOnCreationMessage);
        // What the customer left in their favor is used inside their account's lock, in Vender or their account (Fase 26D).
        RuleFor(x => x.Method).NotEqual(OrderPaymentMethod.CustomerBalance)
            .WithMessage(OrderPaymentMethodRules.CustomerBalanceFromAccountMessage);
        RuleFor(x => x.EvidenceUrl).MaximumLength(1000);
        RuleFor(x => x.Reference).MaximumLength(120);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Tip).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
        RuleFor(x => x.CashReceived!.Value).GreaterThan(0).PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .When(x => x.CashReceived.HasValue).WithName(nameof(AddOrderPaymentCommand.CashReceived));
        RuleFor(x => x.CashReceived).Null().When(x => x.Method != OrderPaymentMethod.Cash)
            .WithMessage(OrderPayment.CashReceivedOnlyForCashMessage);
        RuleFor(x => x.CashReceived!.Value).GreaterThanOrEqualTo(x => x.Amount + x.Tip)
            .When(x => x.CashReceived.HasValue && x.Method == OrderPaymentMethod.Cash)
            .WithName(nameof(AddOrderPaymentCommand.CashReceived))
            .WithMessage(OrderPayment.CashReceivedShortMessage);
    }
}

public class AddOrderPaymentCommandHandler : IRequestHandler<AddOrderPaymentCommand, Result<OrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IOrderCashService _orderCashService;
    private readonly ICurrentUserService? _currentUserService;

    public AddOrderPaymentCommandHandler(
        IApplicationDbContext context,
        IDateTimeProvider dateTimeProvider,
        IOrderCashService orderCashService,
        ICurrentUserService? currentUserService = null)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
        _orderCashService = orderCashService;
        _currentUserService = currentUserService;
    }

    public async Task<Result<OrderDto>> Handle(AddOrderPaymentCommand request, CancellationToken cancellationToken)
    {
        // With its pieces: a service still in the workshop does not close when it is paid (CloseIfPaidCounterSale).
        var order = await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .Include(o => o.ServicePieces)
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
                request.Notes,
                request.Tip,
                request.CashReceived),
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
            .Include(o => o.PlatformDetail)
            .Include(o => o.ServicePieces)
            .Include(o => o.Payments)
            // What was forgiven stays in the order the screen swaps in (Fase 26B).
            .Include(o => o.WriteOffs)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new EntityNotFoundException("Order", request.OrderId);

        var dto = updatedOrder.ToDto();
        dto.ApplyStateMetadata();
        return Result.Success(dto);
    }
}
