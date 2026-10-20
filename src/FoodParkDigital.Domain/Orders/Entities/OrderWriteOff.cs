using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Entities;

/// <summary>
/// What the business forgave of an order a customer owed (Fase 26B, ADR 0034): a separate record with its amount, reason and
/// author, like a refund (ADR 0007). The sale and its payments stay as they were: the write-off only lowers what is still
/// owed, and no money moves in or out of a drawer. A mistake is undone once (<see cref="Order.ReverseWriteOff"/>): the record
/// stays, with when and who undid it, and what it forgave is owed again.
/// </summary>
public class OrderWriteOff : AuditableEntity, ITenantScoped
{
    public const int MaxNotesLength = 500;
    public const string AmountMessage = "Lo condonado debe ser mayor a cero, con máximo 2 decimales.";
    public const string NotesNeededMessage = "Escribe por qué se condona cuando el motivo es «Otro».";
    public const string AlreadyReversedMessage = "Eso ya se deshizo.";

    private OrderWriteOff() { }

    internal OrderWriteOff(
        Guid tenantId,
        Guid orderId,
        decimal amount,
        OrderWriteOffReason reason,
        string? notes,
        DateTimeOffset writtenOffAt,
        string writtenOffBy)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            throw new DomainException(AmountMessage);
        if (!Enum.IsDefined(reason))
            throw new DomainException("El motivo no es válido.");
        var note = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (reason == OrderWriteOffReason.Other && (note is null || note.Length < 3))
            throw new DomainException(NotesNeededMessage);

        TenantId = tenantId;
        OrderId = orderId;
        Amount = amount;
        Reason = reason;
        Notes = note is { Length: > MaxNotesLength } ? note[..MaxNotesLength] : note;
        WrittenOffAt = writtenOffAt;
        WrittenOffBy = writtenOffBy;
    }

    public Guid TenantId { get; private set; }
    public Guid OrderId { get; private set; }
    public decimal Amount { get; private set; }
    public OrderWriteOffReason Reason { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset WrittenOffAt { get; private set; }
    public string WrittenOffBy { get; private set; } = null!;
    /// <summary>When it was undone; null while it stands.</summary>
    public DateTimeOffset? ReversedAt { get; private set; }
    public string? ReversedBy { get; private set; }
    public bool IsReversed => ReversedAt is not null;

    internal void Reverse(DateTimeOffset reversedAt, string reversedBy)
    {
        if (IsReversed)
            throw new DomainException(AlreadyReversedMessage);
        ReversedAt = reversedAt;
        ReversedBy = reversedBy;
    }
}
