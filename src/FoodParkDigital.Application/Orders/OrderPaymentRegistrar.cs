using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders;

/// <summary>A payment captured at the counter, before it is applied to an order.</summary>
public sealed record OrderPaymentInput(
    decimal Amount,
    OrderPaymentMethod Method,
    Guid? InventoryLocationId,
    string? EvidenceUrl,
    string? Reference,
    string? Notes);

/// <summary>
/// Registers payments on an order and in the open cash session: validates the evidence and the branch,
/// stamps the server time and leaves everything to the caller's SaveChanges, so an order and its
/// payments are stored together or not at all.
/// </summary>
public sealed class OrderPaymentRegistrar
{
    private readonly IApplicationDbContext _context;
    private readonly IOrderCashService _orderCashService;
    private readonly IDateTimeProvider _dateTimeProvider;
    public const string NoOpenDrawerMessage = "Abre tu caja para cobrar en efectivo.";

    private Guid? _defaultLocationId;
    private bool _defaultLocationLoaded;
    private TenantSalesSettings? _salesSettings;

    public OrderPaymentRegistrar(
        IApplicationDbContext context,
        IOrderCashService orderCashService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _orderCashService = orderCashService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<OrderPayment>> RegisterAsync(
        Order order,
        OrderPaymentInput input,
        CancellationToken cancellationToken)
    {
        if (order.Status == OrderStatus.Cancelled)
            return Result.Failure<OrderPayment>("No se pueden registrar pagos en una orden cancelada.");

        // The evidence is shown to staff as a link: it must be a file uploaded for this tenant.
        if (!OrderUploadRules.IsOwnPaymentEvidence(input.EvidenceUrl, order.TenantId))
            return Result.Failure<OrderPayment>("La evidencia del pago debe subirse desde el sistema.");

        if (input.Amount > order.Balance)
            return Result.Failure<OrderPayment>("El pago excede el saldo pendiente de la orden.");

        Guid? inventoryLocationId = input.InventoryLocationId;
        if (inventoryLocationId.HasValue)
        {
            var exists = await _context.InventoryLocations
                .AnyAsync(
                    x => x.Id == inventoryLocationId.Value
                      && x.TenantId == order.TenantId
                      && x.IsActive,
                    cancellationToken);

            if (!exists)
                return Result.Failure<OrderPayment>("La sucursal seleccionada no es valida.");
        }
        else
        {
            inventoryLocationId = await GetDefaultLocationIdAsync(order.TenantId, cancellationToken);
        }

        var payment = order.RegisterPayment(
            amount: input.Amount,
            method: input.Method,
            // Stamped by the server: a client-provided date could rewrite already reported cuts.
            paidAt: _dateTimeProvider.UtcNow,
            inventoryLocationId: inventoryLocationId,
            evidenceUrl: input.EvidenceUrl?.Trim(),
            reference: input.Reference?.Trim(),
            notes: input.Notes?.Trim());
        var inDrawer = await _orderCashService.RegisterOrderPaymentAsync(order, payment, cancellationToken);

        // A business may refuse cash that no drawer would count. The order was changed in memory only: the
        // caller does not save after a failure.
        if (!inDrawer && input.Method == OrderPaymentMethod.Cash)
        {
            _salesSettings ??= await TenantSalesSettings.ReadAsync(_context, order.TenantId, cancellationToken);
            if (_salesSettings.RequireOpenCashSession)
                return Result.Failure<OrderPayment>(NoOpenDrawerMessage);
        }

        return Result.Success(payment);
    }

    private async Task<Guid?> GetDefaultLocationIdAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!_defaultLocationLoaded)
        {
            _defaultLocationId = await _context.InventoryLocations
                .Where(x => x.TenantId == tenantId && x.IsActive && x.IsDefault)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            _defaultLocationLoaded = true;
        }

        return _defaultLocationId;
    }
}
