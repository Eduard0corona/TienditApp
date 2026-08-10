using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Finance.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Finance.Commands;

public record AddCashTransactionCommand : IRequest<Result<CashTransactionDto>>
{
    public Guid CashSessionId { get; init; }
    public CashTransactionType TransactionType { get; init; }
    public decimal Amount { get; init; }
    public DateTimeOffset? OccurredAt { get; init; }
    public OrderPaymentMethod? PaymentMethod { get; init; }
    public Guid? InventoryLocationId { get; init; }
    public Guid? OrderId { get; init; }
    public string? Reference { get; init; }
    public string? Notes { get; init; }
}

public class AddCashTransactionCommandValidator : AbstractValidator<AddCashTransactionCommand>
{
    public AddCashTransactionCommandValidator()
    {
        RuleFor(x => x.CashSessionId).NotEmpty();
        RuleFor(x => x.TransactionType).IsInEnum();
        RuleFor(x => x.Amount).NotEqual(0);
        RuleFor(x => x.PaymentMethod).IsInEnum().When(x => x.PaymentMethod.HasValue);
        RuleFor(x => x.InventoryLocationId).NotEmpty().When(x => x.InventoryLocationId.HasValue);
        RuleFor(x => x.Reference).MaximumLength(120);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class AddCashTransactionCommandHandler : IRequestHandler<AddCashTransactionCommand, Result<CashTransactionDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public AddCashTransactionCommandHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<CashTransactionDto>> Handle(AddCashTransactionCommand request, CancellationToken cancellationToken)
    {
        var session = await _context.CashSessions
            .Include(x => x.Transactions)
            .FirstOrDefaultAsync(x => x.Id == request.CashSessionId, cancellationToken)
            ?? throw new EntityNotFoundException("CashSession", request.CashSessionId);

        Guid? inventoryLocationId = request.InventoryLocationId;
        if (inventoryLocationId.HasValue)
        {
            var exists = await _context.InventoryLocations
                .AnyAsync(
                    x => x.Id == inventoryLocationId.Value
                      && x.TenantId == session.TenantId
                      && x.IsActive,
                    cancellationToken);

            if (!exists)
                return Result.Failure<CashTransactionDto>("La sucursal seleccionada no es valida.");
        }
        else
        {
            inventoryLocationId = await _context.InventoryLocations
                .Where(x => x.TenantId == session.TenantId && x.IsActive && x.IsDefault)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        session.RegisterTransaction(
            transactionType: request.TransactionType,
            amount: request.Amount,
            occurredAt: request.OccurredAt ?? _dateTimeProvider.UtcNow,
            paymentMethod: request.PaymentMethod,
            inventoryLocationId: inventoryLocationId,
            orderId: request.OrderId,
            reference: request.Reference,
            notes: request.Notes);

        await _context.SaveChangesAsync(cancellationToken);

        var transaction = session.Transactions
            .OrderByDescending(x => x.CreatedAt)
            .First();

        return Result.Success(new CashTransactionDto
        {
            Id = transaction.Id,
            CashSessionId = transaction.CashSessionId,
            TransactionType = transaction.TransactionType,
            Amount = transaction.Amount,
            OccurredAt = transaction.OccurredAt,
            PaymentMethod = transaction.PaymentMethod,
            InventoryLocationId = transaction.InventoryLocationId,
            OrderId = transaction.OrderId,
            Reference = transaction.Reference,
            Notes = transaction.Notes
        });
    }
}
