using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Finance.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Finance.Entities;
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
        RuleFor(x => x.TransactionType)
            .IsInEnum()
            .Must(CashSession.IsManualTransactionType)
            .WithMessage("Solo se pueden registrar manualmente depositos, gastos, retiros y reembolsos.");
        // The type decides the direction of the movement; the amount is always positive.
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
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

        if (request.OrderId.HasValue
            && !await _context.Orders.AnyAsync(o => o.Id == request.OrderId.Value, cancellationToken))
        {
            return Result.Failure<CashTransactionDto>("La orden indicada no existe.");
        }

        // The movement time is always stamped by the server so that already reported cuts cannot be altered.
        var transaction = session.RegisterTransaction(
            transactionType: request.TransactionType,
            amount: request.Amount,
            occurredAt: _dateTimeProvider.UtcNow,
            paymentMethod: request.PaymentMethod,
            inventoryLocationId: inventoryLocationId,
            orderId: request.OrderId,
            reference: request.Reference,
            notes: request.Notes);

        await _context.SaveChangesAsync(cancellationToken);

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
