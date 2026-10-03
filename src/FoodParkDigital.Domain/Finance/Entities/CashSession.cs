using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Finance.Entities;

public class CashSession : AuditableEntity, ITenantScoped, IAggregateRoot
{
    private readonly List<CashTransaction> _transactions = new();

    private CashSession() { }

    public CashSession(
        Guid tenantId,
        DateTimeOffset openedAt,
        string openedBy,
        decimal openingAmount,
        string? notes = null)
    {
        if (openingAmount < 0)
            throw new DomainException("El fondo inicial no puede ser negativo.");

        TenantId = tenantId;
        OpenedAt = openedAt;
        OpenedBy = openedBy;
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
    public string? Notes { get; private set; }

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
    public decimal NonCashInflows => _transactions.Where(x => x.Amount > 0 && !IsCashMovement(x.PaymentMethod)).Sum(x => x.Amount);
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
    /// The transaction type decides the direction of the money: sales and deposits enter the drawer,
    /// refunds, expenses and withdrawals leave it. Adjustments keep the sign they are given.
    /// </summary>
    public static decimal ToSignedAmount(CashTransactionType transactionType, decimal amount)
    {
        if (amount == 0)
            throw new DomainException("El monto del movimiento debe ser diferente de cero.");

        return transactionType switch
        {
            CashTransactionType.SalePayment or CashTransactionType.Deposit when amount < 0 =>
                throw new DomainException("El monto de un ingreso debe ser mayor a cero."),
            CashTransactionType.SalePayment or CashTransactionType.Deposit => amount,
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

    public void Close(DateTimeOffset closedAt, string closedBy, decimal closingAmount, string? notes = null)
    {
        if (Status != CashSessionStatus.Open)
            throw new DomainException("La caja ya está cerrada.");
        if (closingAmount < 0)
            throw new DomainException("El monto contado al cierre no puede ser negativo.");

        ClosedAt = closedAt;
        ClosedBy = closedBy;
        ClosingAmount = closingAmount;
        Notes = string.IsNullOrWhiteSpace(notes) ? Notes : notes.Trim();
        Status = CashSessionStatus.Closed;
        Revision++;
    }
}
