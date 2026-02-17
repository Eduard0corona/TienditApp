using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Finance.DTOs;
using FoodParkDigital.Domain.Finance.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Finance.Queries;

public record GetCurrentCashSessionQuery : IRequest<CashSessionDto?>;

public class GetCurrentCashSessionQueryHandler : IRequestHandler<GetCurrentCashSessionQuery, CashSessionDto?>
{
    private readonly IApplicationDbContext _context;

    public GetCurrentCashSessionQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CashSessionDto?> Handle(GetCurrentCashSessionQuery request, CancellationToken cancellationToken)
    {
        var session = await _context.CashSessions
            .AsNoTracking()
            .Where(x => x.Status == CashSessionStatus.Open)
            .OrderByDescending(x => x.OpenedAt)
            .Select(x => new
            {
                Entity = x,
                Inflows = _context.CashTransactions.Where(t => t.CashSessionId == x.Id && t.Amount > 0).Sum(t => (decimal?)t.Amount) ?? 0m,
                Outflows = _context.CashTransactions.Where(t => t.CashSessionId == x.Id && t.Amount < 0).Sum(t => (decimal?)(-t.Amount)) ?? 0m
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (session is null)
            return null;

        return new CashSessionDto
        {
            Id = session.Entity.Id,
            OpenedAt = session.Entity.OpenedAt,
            OpenedBy = session.Entity.OpenedBy,
            OpeningAmount = session.Entity.OpeningAmount,
            ClosedAt = session.Entity.ClosedAt,
            ClosedBy = session.Entity.ClosedBy,
            ClosingAmount = session.Entity.ClosingAmount,
            Status = session.Entity.Status,
            Notes = session.Entity.Notes,
            Inflows = session.Inflows,
            Outflows = session.Outflows,
            ExpectedBalance = session.Entity.OpeningAmount + session.Inflows - session.Outflows
        };
    }
}
