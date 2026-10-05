using FoodParkDigital.Application.Audit.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Audit.Queries;

[RequiresPermission(TenantPermission.ViewReports)]
public record GetAuditLogsQuery : IRequest<PaginatedList<AuditLogDto>>
{
    public string? EntityName { get; init; }
    public string? Action { get; init; }
    public DateTimeOffset? FromDate { get; init; }
    public DateTimeOffset? ToDate { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public class GetAuditLogsQueryHandler : IRequestHandler<GetAuditLogsQuery, PaginatedList<AuditLogDto>>
{
    private readonly IApplicationDbContext _context;

    public GetAuditLogsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PaginatedList<AuditLogDto>> Handle(GetAuditLogsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(request.EntityName))
            query = query.Where(a => a.EntityName == request.EntityName);

        if (!string.IsNullOrEmpty(request.Action))
            query = query.Where(a => a.Action == request.Action);

        if (request.FromDate.HasValue)
            query = query.Where(a => a.Timestamp >= request.FromDate.Value);

        if (request.ToDate.HasValue)
            query = query.Where(a => a.Timestamp <= request.ToDate.Value);

        var projectedQuery = query
            .OrderByDescending(a => a.Timestamp)
            .Select(AuditLogMappings.AsDto);

        return await PaginatedList<AuditLogDto>.CreateAsync(
            projectedQuery, request.PageNumber, request.PageSize, cancellationToken);
    }
}
