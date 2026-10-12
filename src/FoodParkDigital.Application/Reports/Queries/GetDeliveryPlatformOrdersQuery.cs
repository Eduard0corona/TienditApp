using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Reports.ReadModels;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Reports.Queries;

/// <summary>
/// The orders of one delivery platform registered in a range of local dates (the current month when omitted), the newest
/// first, each with the amounts it copied when it was registered: to compare folio by folio with the platform's weekly
/// statement. Read only: it imports and reconciles nothing.
/// </summary>
[RequiresPermission(TenantPermission.ViewReports)]
public record GetDeliveryPlatformOrdersQuery : IRequest<PaginatedList<DeliveryPlatformOrderDto>>, IReportDateRange
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Rappi, Uber Eats or DiDi Food.</summary>
    public OrderChannel? Channel { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public Guid? BranchId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = DefaultPageSize;
}

public class GetDeliveryPlatformOrdersQueryValidator : AbstractValidator<GetDeliveryPlatformOrdersQuery>
{
    public const string ChooseAPlatformMessage = "Elige Rappi, Uber Eats o DiDi Food.";

    public GetDeliveryPlatformOrdersQueryValidator()
    {
        RuleFor(x => x.Channel)
            .Must(channel => channel is { } value && OrderChannelRules.IsDeliveryPlatform(value))
            .WithMessage(ChooseAPlatformMessage);
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, GetDeliveryPlatformOrdersQuery.MaxPageSize);
        this.ValidateTheRange();
    }
}

public class GetDeliveryPlatformOrdersQueryHandler
    : IRequestHandler<GetDeliveryPlatformOrdersQuery, PaginatedList<DeliveryPlatformOrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetDeliveryPlatformOrdersQueryHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _tenantService = tenantService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<PaginatedList<DeliveryPlatformOrderDto>> Handle(
        GetDeliveryPlatformOrdersQuery request,
        CancellationToken cancellationToken)
    {
        // The validator asks for the platform; without one there is nothing to list.
        if (request.Channel is not { } channel)
            return new PaginatedList<DeliveryPlatformOrderDto>([], 0, request.PageNumber, request.PageSize);

        var tenantId = _tenantService.TenantId;
        var timeZoneId = await _context.TenantSettings
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => x.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);
        var zone = TenantDateTimeResolver.Resolve(timeZoneId);
        var (from, to) = DailySalesReadModel.ResolveRange(
            request.From, request.To, DailySalesReadModel.LocalToday(zone, _dateTimeProvider.UtcNow));
        var startUtc = DailySalesReadModel.StartUtc(from, zone);
        var endUtc = DailySalesReadModel.EndUtc(to, zone);

        // The kinds of order the business can read, as in the other reports.
        var capabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(_context, tenantId, cancellationToken);
        var retail = TenantCapabilityRules.CanCreateOrderType(capabilities, OrderType.Retail);
        var service = TenantCapabilityRules.CanCreateOrderType(capabilities, OrderType.Service);

        var orders =
            from detail in _context.PlatformOrderDetails.AsNoTracking()
            join order in _context.Orders.AsNoTracking() on detail.OrderId equals order.Id
            where detail.TenantId == tenantId
                && detail.Channel == channel
                && detail.RegisteredAt >= startUtc
                && detail.RegisteredAt <= endUtc
                && ((retail && order.OrderType == OrderType.Retail) || (service && order.OrderType == OrderType.Service))
            select new { detail, order };
        if (request.BranchId is { } branchId)
            orders = orders.Where(x => x.order.BranchId == branchId);

        var rows = orders
            .OrderByDescending(x => x.detail.RegisteredAt)
            .ThenBy(x => x.order.Id)
            .Select(x => new DeliveryPlatformOrderDto
            {
                OrderId = x.order.Id,
                OrderNumber = x.order.OrderNumber,
                ExternalOrderNumber = x.detail.ExternalOrderNumber,
                RegisteredAt = x.detail.RegisteredAt,
                Status = x.order.Status,
                CollectedBy = x.detail.CollectedBy,
                OrderTotal = x.detail.OrderTotal,
                CommissionAndTax = x.detail.Commission + x.detail.CommissionTax,
                Withheld = x.detail.IsrWithheld + x.detail.VatWithheld,
                ExpectedSettlement = x.detail.ExpectedSettlement,
                RefundedAmount = x.order.RefundedAmount
            });

        return await PaginatedList<DeliveryPlatformOrderDto>.CreateAsync(rows, request.PageNumber, request.PageSize, cancellationToken);
    }
}

/// <summary>
/// One order of a delivery platform as it was registered: its folio in the app, who charged it and the amounts its
/// terms gave then, with what was given back of it since.
/// </summary>
public class DeliveryPlatformOrderDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = null!;
    /// <summary>The folio the platform's app shows for the order.</summary>
    public string ExternalOrderNumber { get; set; } = null!;
    public DateTimeOffset RegisteredAt { get; set; }
    public OrderStatus Status { get; set; }
    public PlatformCollectedBy CollectedBy { get; set; }
    /// <summary>The order's total when it was registered.</summary>
    public decimal OrderTotal { get; set; }
    /// <summary>The commission and its IVA: the cost of the channel.</summary>
    public decimal CommissionAndTax { get; set; }
    /// <summary>ISR and IVA withheld: payments on account of the business's taxes, not a cost.</summary>
    public decimal Withheld { get; set; }
    /// <summary>What the platform should deposit for the order; negative when the business charged it.</summary>
    public decimal ExpectedSettlement { get; set; }
    /// <summary>What was given back of the order so far.</summary>
    public decimal RefundedAmount { get; set; }
}
