using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Finance.DTOs;
using FoodParkDigital.Domain.Finance.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Finance.Queries;

public record GetCashTransactionsQuery : IRequest<PaginatedList<CashTransactionDto>>
{
    public Guid? CashSessionId { get; init; }
    public CashTransactionType? TransactionType { get; init; }
    public DateTimeOffset? FromDate { get; init; }
    public DateTimeOffset? ToDate { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetCashTransactionsQueryHandler : IRequestHandler<GetCashTransactionsQuery, PaginatedList<CashTransactionDto>>
{
    private readonly IApplicationDbContext _context;

    public GetCashTransactionsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PaginatedList<CashTransactionDto>> Handle(GetCashTransactionsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.CashTransactions
            .AsNoTracking()
            .AsQueryable();

        if (request.CashSessionId.HasValue)
            query = query.Where(x => x.CashSessionId == request.CashSessionId.Value);
        if (request.TransactionType.HasValue)
            query = query.Where(x => x.TransactionType == request.TransactionType.Value);
        if (request.FromDate.HasValue)
            query = query.Where(x => x.OccurredAt >= request.FromDate.Value);
        if (request.ToDate.HasValue)
            query = query.Where(x => x.OccurredAt <= request.ToDate.Value);

        var projected = query
            .OrderByDescending(x => x.OccurredAt)
            .Select(x => new CashTransactionDto
            {
                Id = x.Id,
                CashSessionId = x.CashSessionId,
                TransactionType = x.TransactionType,
                Amount = x.Amount,
                OccurredAt = x.OccurredAt,
                PaymentMethod = x.PaymentMethod,
                OrderId = x.OrderId,
                Reference = x.Reference,
                Notes = x.Notes
            });

        return await PaginatedList<CashTransactionDto>.CreateAsync(
            projected,
            request.PageNumber,
            request.PageSize,
            cancellationToken);
    }
}
