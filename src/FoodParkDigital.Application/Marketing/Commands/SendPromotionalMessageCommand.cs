using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Marketing.Commands;

public record SendPromotionalMessageCommand : IRequest<Result<int>>
{
    public string Message { get; init; } = null!;
    public int MinimumCompletedOrders { get; init; } = 1;
    public DateTimeOffset? LastPurchaseFrom { get; init; }
}

public class SendPromotionalMessageCommandValidator : AbstractValidator<SendPromotionalMessageCommand>
{
    public SendPromotionalMessageCommandValidator()
    {
        RuleFor(x => x.Message).NotEmpty().MaximumLength(500);
        RuleFor(x => x.MinimumCompletedOrders).GreaterThanOrEqualTo(1);
    }
}

public class SendPromotionalMessageCommandHandler : IRequestHandler<SendPromotionalMessageCommand, Result<int>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IWhatsAppService _whatsAppService;

    public SendPromotionalMessageCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IWhatsAppService whatsAppService)
    {
        _context = context;
        _tenantService = tenantService;
        _whatsAppService = whatsAppService;
    }

    public async Task<Result<int>> Handle(SendPromotionalMessageCommand request, CancellationToken cancellationToken)
    {
        var completedStatuses = new[]
        {
            Domain.Orders.Enums.OrderStatus.Delivered,
            Domain.Orders.Enums.OrderStatus.Completed
        };

        var query = _context.Orders
            .AsNoTracking()
            .Where(x => completedStatuses.Contains(x.Status))
            .AsQueryable();

        if (request.LastPurchaseFrom.HasValue)
            query = query.Where(x => x.CreatedAt >= request.LastPurchaseFrom.Value);

        var targets = await query
            .GroupBy(x => x.CustomerPhone)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() >= request.MinimumCompletedOrders)
            .Select(g => g.Key!)
            .Distinct()
            .Take(300)
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var phone in targets)
        {
            var result = await _whatsAppService.SendTextMessageAsync(
                _tenantService.TenantId,
                phone,
                request.Message,
                cancellationToken);
            if (result.IsSuccess)
                sent += 1;
        }

        return Result.Success(sent);
    }
}
