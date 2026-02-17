using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Finance.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Finance.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Finance.Commands;

public record CloseCashSessionCommand : IRequest<Result<CashSessionDto>>
{
    public Guid CashSessionId { get; init; }
    public decimal ClosingAmount { get; init; }
    public string? Notes { get; init; }
}

public class CloseCashSessionCommandValidator : AbstractValidator<CloseCashSessionCommand>
{
    public CloseCashSessionCommandValidator()
    {
        RuleFor(x => x.CashSessionId).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class CloseCashSessionCommandHandler : IRequestHandler<CloseCashSessionCommand, Result<CashSessionDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CloseCashSessionCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<CashSessionDto>> Handle(CloseCashSessionCommand request, CancellationToken cancellationToken)
    {
        var session = await _context.CashSessions
            .Include(x => x.Transactions)
            .FirstOrDefaultAsync(x => x.Id == request.CashSessionId, cancellationToken)
            ?? throw new EntityNotFoundException("CashSession", request.CashSessionId);

        session.Close(
            closedAt: _dateTimeProvider.UtcNow,
            closedBy: _currentUserService.UserName ?? "system",
            closingAmount: request.ClosingAmount,
            notes: request.Notes);

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new CashSessionDto
        {
            Id = session.Id,
            OpenedAt = session.OpenedAt,
            OpenedBy = session.OpenedBy,
            OpeningAmount = session.OpeningAmount,
            ClosedAt = session.ClosedAt,
            ClosedBy = session.ClosedBy,
            ClosingAmount = session.ClosingAmount,
            Status = session.Status,
            Notes = session.Notes,
            Inflows = session.Inflows,
            Outflows = session.Outflows,
            ExpectedBalance = session.ExpectedBalance
        });
    }
}
