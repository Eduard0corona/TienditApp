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

    public IReadOnlyCollection<CashTransaction> Transactions => _transactions.AsReadOnly();
    public decimal Inflows => _transactions.Where(x => x.Amount > 0).Sum(x => x.Amount);
    public decimal Outflows => _transactions.Where(x => x.Amount < 0).Sum(x => Math.Abs(x.Amount));
    public decimal ExpectedBalance => OpeningAmount + Inflows - Outflows;

    public void RegisterTransaction(
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
        if (amount == 0)
            throw new DomainException("El monto del movimiento debe ser diferente de cero.");

        _transactions.Add(new CashTransaction(
            tenantId: TenantId,
            cashSessionId: Id,
            transactionType: transactionType,
            amount: amount,
            occurredAt: occurredAt,
            paymentMethod: paymentMethod,
            inventoryLocationId: inventoryLocationId,
            orderId: orderId,
            reference: reference,
            notes: notes));
    }

    public void Close(DateTimeOffset closedAt, string closedBy, decimal closingAmount, string? notes = null)
    {
        if (Status != CashSessionStatus.Open)
            throw new DomainException("La caja ya está cerrada.");

        ClosedAt = closedAt;
        ClosedBy = closedBy;
        ClosingAmount = closingAmount;
        Notes = string.IsNullOrWhiteSpace(notes) ? Notes : notes.Trim();
        Status = CashSessionStatus.Closed;
    }
}
