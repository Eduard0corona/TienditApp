using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Finance;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Couriers;

/// <summary>What is answered when a settlement cannot be saved, and how the API tells them apart.</summary>
public static class CourierSettlementErrors
{
    public const string NoOwnDrawer = "Abre tu caja para recibir lo que entrega el repartidor: el efectivo entra a tu caja.";
    public const string NotOwed =
        "Un pedido de la lista ya no se le debe al repartidor (alguien lo cobró, lo liquidó o cambió): lee otra vez la lista.";
    public const string Stale = "Alguien registró un cobro al mismo tiempo: lee otra vez lo que debe el repartidor.";
}

/// <summary>
/// The courier hands over the cash it collected (Fase 22D): each order is paid with its balance in the drawer of whoever
/// receives it, its adjustments (the courier's trip, its fee, a shortage…) enter that drawer as movements, and the
/// settlement keeps it all. What was handed over must be the orders plus the adjustments, to the cent. Charging orders
/// and moving the cash of a drawer are both needed.
/// </summary>
/// <param name="Method">How the courier handed it over: cash, or a transfer.</param>
/// <param name="OrdersTotal">
/// What the orders owed when the person read them: if one was charged meanwhile it owes less, and the settlement is not
/// what the person saw (409, read the list again).
/// </param>
[RequiresPermission(TenantPermission.ChargeOrders)]
public record CreateCourierSettlementCommand(
    DeliveryCourier Courier,
    OrderPaymentMethod Method,
    decimal HandedAmount,
    IReadOnlyList<Guid> OrderIds,
    decimal OrdersTotal,
    IReadOnlyList<CourierSettlementAdjustmentInput> Adjustments,
    string? Reference,
    string? Notes) : IRequest<Result<CourierSettlementDetailDto>>, IRequiresContentPermissions
{
    // The money enters a drawer, with movements of its own.
    public IEnumerable<TenantPermission> RequiredPermissions() => [TenantPermission.ManageCash];
}

public class CreateCourierSettlementCommandValidator : AbstractValidator<CreateCourierSettlementCommand>
{
    public CreateCourierSettlementCommandValidator()
    {
        RuleFor(x => x.Courier).IsInEnum().WithMessage(CourierSettlement.CourierMessage);
        RuleFor(x => x.Method).Must(m => m is OrderPaymentMethod.Cash or OrderPaymentMethod.Transfer)
            .WithMessage(CourierSettlement.MethodMessage);
        RuleFor(x => x.OrderIds).Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(CourierSettlement.NoOrdersMessage)
            .NotEmpty().WithMessage(CourierSettlement.NoOrdersMessage)
            .Must(ids => ids.Count <= CourierSettlement.MaxOrders).WithMessage(CourierSettlement.TooManyOrdersMessage)
            .Must(ids => ids.All(id => id != Guid.Empty) && ids.Distinct().Count() == ids.Count)
            .WithMessage(CourierSettlement.RepeatedOrderMessage);
        RuleFor(x => x.Adjustments).Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(CourierSettlement.AdjustmentKindMessage)
            .Must(adjustments => adjustments.Count <= CourierSettlement.MaxAdjustments)
            .WithMessage(CourierSettlement.TooManyAdjustmentsMessage)
            .Must(adjustments => adjustments.All(a => a is not null)).WithMessage(CourierSettlement.AdjustmentKindMessage);
        RuleFor(x => x.OrdersTotal).GreaterThanOrEqualTo(0m).WithMessage(CourierSettlement.OrderAmountMessage);
        RuleFor(x => x.Reference).MaximumLength(CourierSettlement.MaxReferenceLength + 20)
            .WithMessage(CourierSettlement.ReferenceTooLongMessage);
        RuleFor(x => x.Notes).MaximumLength(CourierSettlement.MaxNotesLength + 20)
            .WithMessage(CourierSettlement.NotesTooLongMessage);
    }
}

public class CreateCourierSettlementCommandHandler
    : IRequestHandler<CreateCourierSettlementCommand, Result<CourierSettlementDetailDto>>
{
    public const string SettlementRegistered = "CourierSettlementRegistered";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _clock;

    public CreateCourierSettlementCommandHandler(
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

    public async Task<Result<CourierSettlementDetailDto>> Handle(
        CreateCourierSettlementCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;

        // Whoever receives the money puts it in their own drawer: never in a colleague's count.
        var userId = Guid.TryParse(_currentUserService.UserId, out var parsed) ? parsed : (Guid?)null;
        var drawer = userId is null ? null : await _context.CashSessions
            .Include(s => s.Transactions)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Status == CashSessionStatus.Open && s.OpenedByUserId == userId,
                cancellationToken);
        if (drawer is null)
            return Result.Failure<CourierSettlementDetailDto>(CourierSettlementErrors.NoOwnDrawer);

        var orders = await _context.Orders
            .Include(o => o.Payments)
            .Where(o => o.TenantId == tenantId && request.OrderIds.Contains(o.Id))
            .ToListAsync(cancellationToken);
        var alreadySettled = await _context.CourierSettlementOrders
            .AnyAsync(line => request.OrderIds.Contains(line.OrderId), cancellationToken);
        if (orders.Count != request.OrderIds.Count || alreadySettled
            || orders.Any(o => o.Delivery?.Courier != request.Courier || !o.AwaitsCourierCash)
            // A balance only goes down: a charge registered since the list was read shows in its total.
            || orders.Sum(o => o.Balance) != request.OrdersTotal)
            return Result.Failure<CourierSettlementDetailDto>(CourierSettlementErrors.NotOwed);

        var now = _clock.UtcNow;
        var courierName = CourierName(request.Courier);
        var lines = orders
            .OrderBy(o => request.OrderIds.ToList().IndexOf(o.Id))
            .Select(o => new CourierSettlementOrderInput(o.Id, o.Balance))
            .ToList();
        CourierSettlement settlement;
        try
        {
            settlement = new CourierSettlement(
                tenantId, request.Courier, drawer.Id, request.Method, request.HandedAmount, lines,
                request.Adjustments, request.Reference, request.Notes, now, _currentUserService.UserName);
        }
        catch (DomainException ex)
        {
            return Result.Failure<CourierSettlementDetailDto>(ex.Message);
        }

        // Each order is paid with what it owed, in this drawer, already loaded: never once more for each order.
        var registrar = new OrderPaymentRegistrar(_context, new OwnDrawer(drawer), _clock);
        foreach (var line in lines)
        {
            var order = orders.Single(o => o.Id == line.OrderId);
            var reference = order.Delivery!.CourierFolio is { } folio ? $"{courierName} {folio}" : courierName;
            var payment = await registrar.RegisterAsync(
                order,
                new OrderPaymentInput(line.Amount, request.Method, null, null, reference, $"Liquidación de {courierName}"),
                cancellationToken);
            if (payment.IsFailure)
                return Result.Failure<CourierSettlementDetailDto>(payment.Error!);
        }

        // What the courier deducted leaves the drawer as an expense; what it added enters as a deposit.
        foreach (var adjustment in settlement.Adjustments)
        {
            drawer.RegisterTransaction(
                transactionType: adjustment.Amount < 0 ? CashTransactionType.Expense : CashTransactionType.Deposit,
                amount: Math.Abs(adjustment.Amount),
                occurredAt: now,
                paymentMethod: request.Method,
                reference: courierName,
                notes: AdjustmentNote(adjustment.Kind, adjustment.Description, courierName));
        }

        _context.CourierSettlements.Add(settlement);
        _context.AuditLogs.Add(new AuditLog(
            tenantId: tenantId,
            entityName: nameof(CourierSettlement),
            entityId: settlement.Id.ToString(),
            action: SettlementRegistered,
            performedBy: _currentUserService.UserName ?? "system",
            metadata: JsonSerializer.Serialize(new
            {
                Courier = request.Courier.ToString(),
                Method = request.Method.ToString(),
                settlement.HandedAmount,
                settlement.OrdersTotal,
                settlement.AdjustmentsTotal,
                Orders = orders.Select(o => new { o.OrderNumber, Amount = lines.Single(l => l.OrderId == o.Id).Amount }),
                Adjustments = settlement.Adjustments.Select(a => new { Kind = a.Kind.ToString(), a.Description, a.Amount }),
                settlement.Reference
            })));

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // An order charged, or the drawer moved, after this request read them.
            return Result.Failure<CourierSettlementDetailDto>(CourierSettlementErrors.Stale);
        }
        catch (DbUpdateException)
        {
            // Another settlement took an order first (IX_CourierSettlementOrders_OrderId); anything else is thrown on.
            if (await SettledMeanwhileAsync(request.OrderIds, cancellationToken))
                return Result.Failure<CourierSettlementDetailDto>(CourierSettlementErrors.NotOwed);
            throw;
        }

        return Result.Success(await CourierSettlementReader.DetailAsync(_context, settlement.Id, cancellationToken));
    }

    public static string CourierName(DeliveryCourier courier) => courier switch
    {
        DeliveryCourier.Paquetenvia => "Paquetenvia",
        _ => courier.ToString()
    };

    /// <summary>«Viaje de Paquetenvia», «Faltante de Paquetenvia: no traía cambio»… for the drawer's movement.</summary>
    public static string AdjustmentNote(CourierSettlementAdjustmentKind kind, string? description, string courierName)
    {
        var label = kind switch
        {
            CourierSettlementAdjustmentKind.TripFee => $"Viaje de {courierName}",
            CourierSettlementAdjustmentKind.CollectionFee => $"Comisión por cobrar de {courierName}",
            CourierSettlementAdjustmentKind.Shortage => $"Faltante de {courierName}",
            _ => courierName
        };
        return description is null ? label : $"{label}: {description}";
    }

    private async Task<bool> SettledMeanwhileAsync(IReadOnlyList<Guid> orderIds, CancellationToken cancellationToken)
        => await _context.CourierSettlementOrders
            .AsNoTracking()
            .AnyAsync(line => orderIds.Contains(line.OrderId), cancellationToken);

    /// <summary>The drawer of whoever receives the cash takes every payment of the settlement.</summary>
    private sealed class OwnDrawer(CashSession drawer) : IOrderCashService
    {
        public Task<bool> RegisterOrderPaymentAsync(Order order, OrderPayment payment, CancellationToken cancellationToken)
        {
            OrderCashService.RecordPayment(drawer, order, payment);
            return Task.FromResult(true);
        }

        public Task<bool> RegisterOrderRefundAsync(Order order, OrderRefund refund, CancellationToken cancellationToken)
            => throw new NotSupportedException("A settlement only charges orders.");

        public Task<Guid?> RegisterCustomerBalanceAsync(
            Guid tenantId,
            CashTransactionType transactionType,
            decimal amount,
            OrderPaymentMethod method,
            DateTimeOffset occurredAt,
            string? reference,
            string? notes,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("A settlement only charges orders.");
    }
}
