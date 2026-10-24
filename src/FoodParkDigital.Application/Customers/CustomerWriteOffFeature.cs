using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Customers;

/// <summary>
/// Condonar (Fase 26B, ADR 0034): the owner or an admin forgives part or all of what a customer owes, from their account.
/// One amount, on one order or split over the orders owed from the oldest, each part a record apart on its order
/// (<see cref="Order.WriteOff"/>) that lowers its balance. No money moves: the drawer, the payments and the sales stay as
/// they were. Inside the customer's credit lock, like a payment, so the two count each other.
/// </summary>
[RequiresTenantManager]
public record WriteOffCustomerAccountCommand : IRequest<Result<CustomerAccountDto>>, ITransactionalRequest
{
    public Guid? CustomerId { get; init; }
    public string? Phone { get; init; }
    /// <summary>One order of the account; without it, the amount goes over the orders owed from the oldest.</summary>
    public Guid? OrderId { get; init; }
    public decimal Amount { get; init; }
    public OrderWriteOffReason Reason { get; init; }
    /// <summary>Why, in the owner's words; needed when the reason is «Otro».</summary>
    public string? Notes { get; init; }
    /// <summary>
    /// What the account owed as the screen showed it. A write-off sent twice, or after a payment the screen did not see,
    /// finds the account changed and is refused instead of forgiving twice.
    /// </summary>
    public decimal ExpectedOwed { get; init; }
}

public class WriteOffCustomerAccountCommandValidator : AbstractValidator<WriteOffCustomerAccountCommand>
{
    public WriteOffCustomerAccountCommandValidator()
    {
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x).Must(x => x.CustomerId.HasValue || !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage(CreditAccount.NeedsCustomerMessage);
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage(OrderWriteOff.AmountMessage);
        RuleFor(x => x.Reason).IsInEnum();
        RuleFor(x => x.Notes).MaximumLength(OrderWriteOff.MaxNotesLength);
        RuleFor(x => x.Notes).Must(notes => notes is { } text && text.Trim().Length >= 3)
            .When(x => x.Reason == OrderWriteOffReason.Other)
            .WithMessage(OrderWriteOff.NotesNeededMessage);
        RuleFor(x => x.ExpectedOwed).GreaterThan(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
    }
}

public class WriteOffCustomerAccountCommandHandler : IRequestHandler<WriteOffCustomerAccountCommand, Result<CustomerAccountDto>>
{
    public const string NothingOwedMessage = "No debe nada: no hay nada que condonar.";
    public const string OrderNotOwedMessage = "Ese pedido no está entre lo que debe el cliente.";
    // Orders split in one write-off: far more than a customer owes at once.
    private const int MaxOrders = 500;

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IPhoneNormalizationService _phones;
    private readonly IDateTimeProvider _clock;
    private readonly ICustomerCreditLock _creditLock;

    public WriteOffCustomerAccountCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser,
        IPhoneNormalizationService phones,
        IDateTimeProvider clock,
        ICustomerCreditLock creditLock)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
        _phones = phones;
        _clock = clock;
        _creditLock = creditLock;
    }

    public static string AboveOwedMessage(decimal owed) =>
        $"Debe {OrderStatusLabels.FormatMoney(owed)}: no se puede condonar más.";

    public static string OwedChangedMessage(decimal owed) =>
        $"La cuenta cambió: ahora debe {OrderStatusLabels.FormatMoney(owed)}. Revisa el estado de cuenta antes de condonar.";

    public static string AboveSplitMessage(decimal splittable) =>
        $"Condona hasta {OrderStatusLabels.FormatMoney(splittable)} a la vez: la cuenta tiene más de {MaxOrders} pedidos.";

    public async Task<Result<CustomerAccountDto>> Handle(WriteOffCustomerAccountCommand request, CancellationToken cancellationToken)
    {
        var resolved = await CreditAccount.ResolveAsync(_context, _phones, request.CustomerId, request.Phone, cancellationToken);
        if (resolved.IsFailure)
            return Result.Failure<CustomerAccountDto>(resolved.Error!);
        var account = resolved.Value;

        // A payment or a sale on credit for the same customer waits: each one counts what the other left.
        await _creditLock.AcquireAsync(_tenant.TenantId, account.Phone, cancellationToken);

        var variants = MessagingPhone.Variants(account.Phone).ToList();
        // With the delivery: what a courier collects is not the customer's to be forgiven (Order.IsOwedByCustomer).
        var orders = await _context.Orders
            .Include(o => o.Delivery)
            .Where(o => o.CustomerPhone != null && variants.Contains(o.CustomerPhone))
            .Where(Receivables.IsOwed)
            .OrderBy(o => o.CreatedAt)
            .ThenBy(o => o.OrderNumber)
            .Take(MaxOrders)
            .ToListAsync(cancellationToken);

        // All of it, as the account shows it, though one write-off splits over the oldest orders only.
        var owed = await Receivables.OwedAsync(_context.Orders, account.Phone, cancellationToken);
        if (owed == 0)
            return Result.Failure<CustomerAccountDto>(NothingOwedMessage);
        if (request.ExpectedOwed != owed)
            return Result.Failure<CustomerAccountDto>(OwedChangedMessage(owed));
        if (request.Amount > owed)
            return Result.Failure<CustomerAccountDto>(AboveOwedMessage(owed));
        if (request.OrderId is null && request.Amount > orders.Sum(o => o.Balance))
            return Result.Failure<CustomerAccountDto>(AboveSplitMessage(orders.Sum(o => o.Balance)));

        var targets = orders;
        if (request.OrderId is { } orderId)
        {
            var order = orders.FirstOrDefault(o => o.Id == orderId);
            if (order is null)
                return Result.Failure<CustomerAccountDto>(OrderNotOwedMessage);
            targets = [order];
        }

        var by = _currentUser.UserName ?? "system";
        var now = _clock.UtcNow;
        var remaining = request.Amount;
        var parts = new List<object>();
        try
        {
            foreach (var order in targets)
            {
                if (remaining == 0)
                    break;

                // One order takes all of it (and refuses more than it owes); the account goes from the oldest.
                var part = request.OrderId is null ? Math.Min(remaining, order.Balance) : remaining;
                order.WriteOff(part, request.Reason, request.Notes, now, by);
                remaining -= part;
                parts.Add(new { order.OrderNumber, Amount = part });
            }
        }
        catch (DomainException refused)
        {
            return Result.Failure<CustomerAccountDto>(refused.Message);
        }

        // Forgiving a debt is the owner's decision: under the customer record, or the first order when there is none.
        _context.AuditLogs.Add(new AuditLog(
            tenantId: _tenant.TenantId,
            entityName: account.Customer is null ? nameof(Order) : nameof(Customer),
            entityId: (account.Customer?.Id ?? targets[0].Id).ToString(),
            action: "CustomerAccountWrittenOff",
            performedBy: by,
            metadata: JsonSerializer.Serialize(new { request.Amount, Reason = request.Reason.ToString(), Orders = parts })));
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await account.ReadAsync(_context, now, cancellationToken));
    }
}

/// <summary>
/// Deshacer lo condonado (Fase 26B, ADR 0034): the owner or an admin undoes a write-off made by mistake. Its record stays,
/// with when and who undid it, and what it forgave is owed again (<see cref="Order.ReverseWriteOff"/>). Once: a write-off
/// undone is refused the second time, so sending it twice undoes it once. Inside the customer's credit lock, like a write-off.
/// While its credit note stands with the SAT (Fase 28D) it is not undone: the note is cancelled first, under the business's
/// invoicing lock, so no note is issued for it meanwhile.
/// </summary>
[RequiresTenantManager]
public record ReverseCustomerWriteOffCommand : IRequest<Result<CustomerAccountDto>>, ITransactionalRequest
{
    public Guid WriteOffId { get; init; }
}

public class ReverseCustomerWriteOffCommandHandler : IRequestHandler<ReverseCustomerWriteOffCommand, Result<CustomerAccountDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IPhoneNormalizationService _phones;
    private readonly IDateTimeProvider _clock;
    private readonly ICustomerCreditLock _creditLock;
    private readonly IInvoicingLock _invoicingLock;

    public static string LiveCreditNoteMessage(string number) =>
        $"Esta condonación tiene la nota de crédito {number} vigente: cancélala en Pedidos › Factura antes de deshacerla.";

    public static string ComplementsCountingItMessage(IReadOnlyList<string> complements) =>
        $"Los complementos de pago {string.Join(", ", complements)} descuentan la nota de crédito de esta condonación de su " +
        "saldo: cancélalos en Pedidos › Factura antes de deshacerla, y se emiten otra vez sin ella.";

    public ReverseCustomerWriteOffCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser,
        IPhoneNormalizationService phones,
        IDateTimeProvider clock,
        ICustomerCreditLock creditLock,
        IInvoicingLock invoicingLock)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
        _phones = phones;
        _clock = clock;
        _creditLock = creditLock;
        _invoicingLock = invoicingLock;
    }

    public async Task<Result<CustomerAccountDto>> Handle(ReverseCustomerWriteOffCommand request, CancellationToken cancellationToken)
    {
        // The order it forgave, of this business (the tenant filter): another business's write-off is not found.
        var target = await _context.Orders.AsNoTracking()
            .Where(o => o.WriteOffs.Any(w => w.Id == request.WriteOffId))
            .Select(o => new { o.Id, o.CustomerPhone })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException(nameof(OrderWriteOff), request.WriteOffId);

        var resolved = await CreditAccount.ResolveAsync(_context, _phones, null, target.CustomerPhone, cancellationToken);
        if (resolved.IsFailure)
            return Result.Failure<CustomerAccountDto>(resolved.Error!);
        var account = resolved.Value;

        // A payment, a sale on credit or a write-off for the same customer waits: each one counts what the other left.
        await _creditLock.AcquireAsync(_tenant.TenantId, account.Phone, cancellationToken);
        // And its credit note (Fase 28D): none is issued for it while it is undone.
        await _invoicingLock.AcquireAsync(_tenant.TenantId, cancellationToken);
        if (await CreditNotes.LiveOfWriteOffAsync(_context, request.WriteOffId, cancellationToken) is { } note)
            return Result.Failure<CustomerAccountDto>(LiveCreditNoteMessage(note.Number));
        if (await CreditNotes.ComplementsCountingWriteOffAsync(_context, request.WriteOffId, cancellationToken) is { Count: > 0 } complements)
            return Result.Failure<CustomerAccountDto>(ComplementsCountingItMessage(complements));
        var order = await _context.Orders
            .Include(o => o.WriteOffs)
            .FirstAsync(o => o.Id == target.Id, cancellationToken);

        var by = _currentUser.UserName ?? "system";
        var now = _clock.UtcNow;
        OrderWriteOff reversed;
        try
        {
            reversed = order.ReverseWriteOff(request.WriteOffId, now, by);
        }
        catch (DomainException refused)
        {
            return Result.Failure<CustomerAccountDto>(refused.Message);
        }

        _context.AuditLogs.Add(new AuditLog(
            tenantId: _tenant.TenantId,
            entityName: account.Customer is null ? nameof(Order) : nameof(Customer),
            entityId: (account.Customer?.Id ?? order.Id).ToString(),
            action: "CustomerAccountWriteOffReversed",
            performedBy: by,
            metadata: JsonSerializer.Serialize(new { order.OrderNumber, reversed.Amount, WriteOffId = reversed.Id })));
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await account.ReadAsync(_context, now, cancellationToken));
    }
}

/// <summary>
/// How many days the business gives its customers to pay what they owe (Fase 26B, ADR 0034), or no term: owners and admins.
/// It only tells what is overdue in Por cobrar, the account and Vender; it charges no interest and stops no sale.
/// </summary>
[RequiresTenantManager]
public record UpdateCustomerCreditTermCommand : IRequest<Result<CustomerCreditTermDto>>
{
    public int? CreditTermDays { get; init; }
}

public class UpdateCustomerCreditTermCommandValidator : AbstractValidator<UpdateCustomerCreditTermCommand>
{
    public UpdateCustomerCreditTermCommandValidator()
    {
        RuleFor(x => x.CreditTermDays!.Value)
            .InclusiveBetween(TenantSettings.MinCreditTermDays, TenantSettings.MaxCreditTermDays)
            .When(x => x.CreditTermDays.HasValue)
            .WithName(nameof(UpdateCustomerCreditTermCommand.CreditTermDays))
            .WithMessage(TenantSettings.CreditTermRangeMessage);
    }
}

public class UpdateCustomerCreditTermCommandHandler : IRequestHandler<UpdateCustomerCreditTermCommand, Result<CustomerCreditTermDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;

    public UpdateCustomerCreditTermCommandHandler(IApplicationDbContext context, ICurrentTenantService tenant, ICurrentUserService currentUser)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
    }

    public async Task<Result<CustomerCreditTermDto>> Handle(UpdateCustomerCreditTermCommand request, CancellationToken cancellationToken)
    {
        var settings = await _context.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == _tenant.TenantId, cancellationToken);
        if (settings is null)
            return Result.Failure<CustomerCreditTermDto>("Configura primero tu negocio.");

        var previous = settings.CreditTermDays;
        try
        {
            settings.SetCreditTermDays(request.CreditTermDays);
        }
        catch (DomainException refused)
        {
            return Result.Failure<CustomerCreditTermDto>(refused.Message);
        }

        if (previous != settings.CreditTermDays)
        {
            _context.AuditLogs.Add(new AuditLog(
                tenantId: settings.TenantId,
                entityName: nameof(TenantSettings),
                entityId: settings.Id.ToString(),
                action: "CustomerCreditTermChanged",
                performedBy: _currentUser.UserName ?? "system",
                metadata: JsonSerializer.Serialize(new { Previous = previous, New = settings.CreditTermDays })));
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(new CustomerCreditTermDto { CreditTermDays = settings.CreditTermDays });
    }
}
