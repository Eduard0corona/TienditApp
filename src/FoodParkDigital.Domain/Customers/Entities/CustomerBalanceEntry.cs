using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Customers.Enums;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Customers.Entities;

/// <summary>
/// One movement of the balance in a customer's favor (Fase 26D, ADR 0036): what they left, what a refund gave them, what they
/// paid with it and what was given back. Like a refund (ADR 0007), it is never edited or deleted: the balance is the sum of
/// the movements of the account, which is the phone their orders are grouped by (as the fiado of Fase 26A), and it never
/// goes below zero. Money only moves through a drawer when they leave it or get it back; paying with it records the sale.
/// </summary>
public class CustomerBalanceEntry : AuditableEntity, ITenantScoped
{
    public const int MaxPhoneLength = 20;
    public const int MaxReferenceLength = 100;
    public const int MaxNotesLength = 500;
    /// <summary>The most one movement moves, as the most a business lets a customer owe (Fase 26A).</summary>
    public const decimal MaxAmount = 1_000_000m;
    public const string AmountMessage = "El monto va de $0.01 a $1,000,000, con máximo 2 decimales.";
    public const string PhoneMessage = "El saldo a favor es de la cuenta de un cliente: necesita su teléfono.";
    public const string InPersonMethodMessage = "Elige cómo entra o sale el dinero: efectivo, tarjeta, transferencia, billetera u otro.";
    public const string NotEnoughMessage = "No le alcanza el saldo a favor: tiene {0}.";

    private CustomerBalanceEntry() { }

    private CustomerBalanceEntry(
        Guid tenantId,
        string phone,
        Guid? customerId,
        CustomerBalanceEntryKind kind,
        decimal amount,
        DateTimeOffset occurredAt,
        string recordedBy)
    {
        if (amount <= 0 || amount > MaxAmount || decimal.Round(amount, 2) != amount)
            throw new DomainException(AmountMessage);
        if (string.IsNullOrWhiteSpace(phone) || phone.Trim().Length > MaxPhoneLength)
            throw new DomainException(PhoneMessage);

        TenantId = tenantId;
        Phone = phone.Trim();
        CustomerId = customerId;
        Kind = kind;
        // What enters their balance adds to it; what leaves it subtracts.
        Amount = kind is CustomerBalanceEntryKind.Deposit or CustomerBalanceEntryKind.Refund ? amount : -amount;
        OccurredAt = occurredAt;
        RecordedBy = recordedBy;
    }

    public Guid TenantId { get; private set; }
    /// <summary>The account: the customer's phone key, as their orders are grouped by it.</summary>
    public string Phone { get; private set; } = null!;
    public Guid? CustomerId { get; private set; }
    public CustomerBalanceEntryKind Kind { get; private set; }
    /// <summary>Positive when it adds to the balance, negative when it takes from it.</summary>
    public decimal Amount { get; private set; }
    /// <summary>How the money entered or left a drawer, for what they left or got back; null otherwise.</summary>
    public OrderPaymentMethod? Method { get; private set; }
    public Guid? CashSessionId { get; private set; }
    public Guid? OrderId { get; private set; }
    public Guid? OrderPaymentId { get; private set; }
    public Guid? OrderRefundId { get; private set; }
    public string? Reference { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string RecordedBy { get; private set; } = null!;

    /// <summary>What the customer leaves in their favor, through the drawer of whoever charges.</summary>
    public static CustomerBalanceEntry Deposit(
        Guid tenantId, string phone, Guid? customerId, decimal amount, OrderPaymentMethod method, Guid cashSessionId,
        string? reference, string? notes, DateTimeOffset occurredAt, string recordedBy)
        => InPerson(tenantId, phone, customerId, CustomerBalanceEntryKind.Deposit, amount, method, cashSessionId,
            reference, notes, occurredAt, recordedBy);

    /// <summary>What is given back to the customer of their balance, out of the drawer of whoever gives it.</summary>
    public static CustomerBalanceEntry Returned(
        Guid tenantId, string phone, Guid? customerId, decimal amount, OrderPaymentMethod method, Guid cashSessionId,
        string? reference, string? notes, DateTimeOffset occurredAt, string recordedBy)
        => InPerson(tenantId, phone, customerId, CustomerBalanceEntryKind.Returned, amount, method, cashSessionId,
            reference, notes, occurredAt, recordedBy);

    /// <summary>What the customer paid of an order with their balance.</summary>
    public static CustomerBalanceEntry Applied(
        Guid tenantId, string phone, Guid? customerId, decimal amount, Guid orderId, Guid orderPaymentId,
        DateTimeOffset occurredAt, string recordedBy)
        => new(tenantId, phone, customerId, CustomerBalanceEntryKind.Applied, amount, occurredAt, recordedBy)
        {
            OrderId = orderId,
            OrderPaymentId = orderPaymentId
        };

    /// <summary>What a refund of an order gave back to the customer's balance instead of out of a drawer.</summary>
    public static CustomerBalanceEntry Refunded(
        Guid tenantId, string phone, Guid? customerId, decimal amount, Guid orderId, Guid orderRefundId,
        DateTimeOffset occurredAt, string recordedBy)
        => new(tenantId, phone, customerId, CustomerBalanceEntryKind.Refund, amount, occurredAt, recordedBy)
        {
            OrderId = orderId,
            OrderRefundId = orderRefundId
        };

    /// <summary>
    /// What leaves the balance never takes it below zero: the caller reads the balance inside the account's lock and passes it
    /// here before recording the movement.
    /// </summary>
    public static void EnsureCovers(decimal balance, decimal amount, Func<decimal, string> formatMoney)
    {
        if (amount > balance)
            throw new DomainException(string.Format(NotEnoughMessage, formatMoney(Math.Max(0, balance))));
    }

    private static CustomerBalanceEntry InPerson(
        Guid tenantId, string phone, Guid? customerId, CustomerBalanceEntryKind kind, decimal amount, OrderPaymentMethod method,
        Guid cashSessionId, string? reference, string? notes, DateTimeOffset occurredAt, string recordedBy)
    {
        if (!Enum.IsDefined(method) || !OrderPaymentMethodRules.EntersADrawer(method))
            throw new DomainException(InPersonMethodMessage);
        return new CustomerBalanceEntry(tenantId, phone, customerId, kind, amount, occurredAt, recordedBy)
        {
            Method = method,
            CashSessionId = cashSessionId,
            Reference = Clip(reference, MaxReferenceLength),
            Notes = Clip(notes, MaxNotesLength)
        };
    }

    private static string? Clip(string? text, int max)
    {
        var trimmed = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        return trimmed is { Length: var length } && length > max ? trimmed[..max] : trimmed;
    }
}
