using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Finance.Entities;

public class CashSession : AuditableEntity, ITenantScoped, IAggregateRoot
{
    private readonly List<CashTransaction> _transactions = new();

    private CashSession() { }

    /// <param name="openedByUserId">The cashier who owns the drawer: payments they take enter this session.</param>
    /// <param name="inventoryLocationId">Stock location of the branch, kept for the cuts by location.</param>
    /// <param name="branchId">The branch where the drawer is.</param>
    public CashSession(
        Guid tenantId,
        DateTimeOffset openedAt,
        string openedBy,
        decimal openingAmount,
        string? notes = null,
        Guid? openedByUserId = null,
        Guid? inventoryLocationId = null,
        Guid? branchId = null)
    {
        if (openingAmount < 0)
            throw new DomainException("El fondo inicial no puede ser negativo.");

        TenantId = tenantId;
        OpenedAt = openedAt;
        OpenedBy = openedBy;
        OpenedByUserId = openedByUserId;
        InventoryLocationId = inventoryLocationId;
        BranchId = branchId;
        OpeningAmount = openingAmount;
        Notes = notes?.Trim();
        Status = CashSessionStatus.Open;
    }

    public Guid TenantId { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }
    public string OpenedBy { get; private set; } = null!;
    public decimal OpeningAmount { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public string? ClosedBy { get; private set; }
    public decimal? ClosingAmount { get; private set; }
    public CashSessionStatus Status { get; private set; }
    /// <summary>Notes captured when the drawer was opened.</summary>
    public string? Notes { get; private set; }

    /// <summary>Cashier who owns the drawer. Null for drawers opened before per-cashier sessions.</summary>
    public Guid? OpenedByUserId { get; private set; }

    /// <summary>Stock location of the drawer's branch (before branches existed, it was the branch itself).</summary>
    public Guid? InventoryLocationId { get; private set; }

    /// <summary>Branch of the drawer: payments of sales made there enter it.</summary>
    public Guid? BranchId { get; private set; }

    /// <summary>Cash the drawer should have held at closing (opening amount plus cash in, minus cash out).</summary>
    public decimal? ExpectedClosingAmount { get; private set; }

    /// <summary>Counted minus expected at closing: negative is a shortage, positive a surplus.</summary>
    public decimal? ClosingDifference { get; private set; }

    public string? ClosingNotes { get; private set; }

    /// <summary>Optional count by denomination, as JSON (<c>[{"denomination":500,"quantity":2}]</c>).</summary>
    public string? CashCountJson { get; private set; }

    /// <summary>
    /// Optimistic concurrency token: every movement and the close increment it, so a payment that races
    /// a close (or two closes) cannot both succeed.
    /// </summary>
    public int Revision { get; private set; }

    public IReadOnlyCollection<CashTransaction> Transactions => _transactions.AsReadOnly();

    // Inflows, outflows and the expected balance describe the physical cash in the drawer: manual movements
    // and payments made in cash. Card, transfer and wallet payments are reported apart as NonCashInflows.
    public decimal Inflows => _transactions.Where(x => x.Amount > 0 && IsCashMovement(x.PaymentMethod)).Sum(x => x.Amount);
    public decimal Outflows => _transactions.Where(x => x.Amount < 0 && IsCashMovement(x.PaymentMethod)).Sum(x => -x.Amount);
    // Net of refunds made with those methods.
    public decimal NonCashInflows => _transactions.Where(x => !IsCashMovement(x.PaymentMethod)).Sum(x => x.Amount);
    public decimal ExpectedBalance => OpeningAmount + Inflows - Outflows;

    public static bool IsCashMovement(OrderPaymentMethod? paymentMethod) =>
        paymentMethod is null or OrderPaymentMethod.Cash;

    /// <summary>
    /// Movements a user may register by hand. Sale payments only come from order payments.
    /// </summary>
    public static bool IsManualTransactionType(CashTransactionType transactionType) =>
        transactionType is CashTransactionType.Deposit
            or CashTransactionType.Expense
            or CashTransactionType.Withdrawal
            or CashTransactionType.Refund;

    /// <summary>
    /// The transaction type decides the direction of the money: sales, tips and deposits enter the drawer,
    /// refunds, expenses and withdrawals leave it. Adjustments keep the sign they are given.
    /// </summary>
    public static decimal ToSignedAmount(CashTransactionType transactionType, decimal amount)
    {
        if (amount == 0)
            throw new DomainException("El monto del movimiento debe ser diferente de cero.");

        return transactionType switch
        {
            CashTransactionType.SalePayment or CashTransactionType.Deposit or CashTransactionType.Tip when amount < 0 =>
                throw new DomainException("El monto de un ingreso debe ser mayor a cero."),
            CashTransactionType.SalePayment or CashTransactionType.Deposit or CashTransactionType.Tip => amount,
            CashTransactionType.Refund or CashTransactionType.Expense or CashTransactionType.Withdrawal => -Math.Abs(amount),
            _ => amount
        };
    }

    public CashTransaction RegisterTransaction(
        CashTransactionType transactionType,
        decimal amount,
        DateTimeOffset occurredAt,
        OrderPaymentMethod? paymentMethod = null,
        Guid? inventoryLocationId = null,
        Guid? orderId = null,
        string? reference = null,
        string? notes = null)
    {
        if (Status != CashSessionStatus.Open)
            throw new DomainException("No se pueden registrar movimientos en una caja cerrada.");

        var transaction = new CashTransaction(
            tenantId: TenantId,
            cashSessionId: Id,
            transactionType: transactionType,
            amount: ToSignedAmount(transactionType, amount),
            occurredAt: occurredAt,
            paymentMethod: paymentMethod,
            inventoryLocationId: inventoryLocationId,
            orderId: orderId,
            reference: reference,
            notes: notes);

        _transactions.Add(transaction);
        Revision++;
        return transaction;
    }

    /// <summary>
    /// Closes the drawer with the counted cash and keeps the expected amount and the difference, so the
    /// shift history shows shortages and surpluses. A difference needs an explanation.
    /// Requires <see cref="Transactions"/> to be loaded.
    /// </summary>
    public void Close(
        DateTimeOffset closedAt,
        string closedBy,
        decimal closingAmount,
        string? notes = null,
        string? cashCountJson = null)
    {
        if (Status != CashSessionStatus.Open)
            throw new DomainException("La caja ya está cerrada.");
        if (closingAmount < 0)
            throw new DomainException("El monto contado al cierre no puede ser negativo.");

        var expected = ExpectedBalance;
        var difference = closingAmount - expected;
        if (difference != 0 && string.IsNullOrWhiteSpace(notes))
            throw new DomainException(difference < 0
                ? "Explica el faltante en las notas del cierre."
                : "Explica el sobrante en las notas del cierre.");

        ClosedAt = closedAt;
        ClosedBy = closedBy;
        ClosingAmount = closingAmount;
        ExpectedClosingAmount = expected;
        ClosingDifference = difference;
        ClosingNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        CashCountJson = string.IsNullOrWhiteSpace(cashCountJson) ? null : cashCountJson;
        Status = CashSessionStatus.Closed;
        Revision++;
    }

    public bool IsOwnedBy(Guid? userId) => OpenedByUserId is not null && OpenedByUserId == userId;
}
