using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Finance.DTOs;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Finance.Queries;

[RequiresPermission(TenantPermission.ManageCash)]
public record GetCashTransactionsQuery : IRequest<PaginatedList<CashTransactionDto>>
{
    public Guid? CashSessionId { get; init; }
    public CashTransactionType? TransactionType { get; init; }
    public DateTimeOffset? FromDate { get; init; }
    public DateTimeOffset? ToDate { get; init; }
    /// <summary>Reference, notes or who registered it, over every page.</summary>
    public string? Search { get; init; }
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
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(x =>
                (x.Reference != null && x.Reference.ToLower().Contains(search)) ||
                (x.Notes != null && x.Notes.ToLower().Contains(search)) ||
                (x.CreatedBy != null && x.CreatedBy.ToLower().Contains(search)));
        }

        var projected = query
            .OrderByDescending(x => x.OccurredAt)
            .Select(x => new
            {
                Transaction = x,
                LocationName = _context.InventoryLocations
                    .Where(l => x.InventoryLocationId.HasValue && l.Id == x.InventoryLocationId.Value)
                    .Select(l => l.Name)
                    .FirstOrDefault()
            })
            .Select(x => new CashTransactionDto
            {
                Id = x.Transaction.Id,
                CashSessionId = x.Transaction.CashSessionId,
                TransactionType = x.Transaction.TransactionType,
                Amount = x.Transaction.Amount,
                OccurredAt = x.Transaction.OccurredAt,
                PaymentMethod = x.Transaction.PaymentMethod,
                InventoryLocationId = x.Transaction.InventoryLocationId,
                InventoryLocationName = x.LocationName,
                OrderId = x.Transaction.OrderId,
                Reference = x.Transaction.Reference,
                Notes = x.Transaction.Notes,
                CreatedBy = x.Transaction.CreatedBy
            });

        return await PaginatedList<CashTransactionDto>.CreateAsync(
            projected,
            request.PageNumber,
            request.PageSize,
            cancellationToken);
    }
}
