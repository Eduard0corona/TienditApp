using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Messaging.Campaigns;
using FoodParkDigital.Application.Messaging.DTOs;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Messaging.OptOuts;

public record GetMessagingOptOutsQuery : IRequest<PaginatedList<MessagingOptOutDto>>
{
    public string? Search { get; init; }
    /// <summary>Also customers who opted back in.</summary>
    public bool IncludeResubscribed { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetMessagingOptOutsQueryValidator : AbstractValidator<GetMessagingOptOutsQuery>
{
    public GetMessagingOptOutsQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(30);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}

public class GetMessagingOptOutsQueryHandler : IRequestHandler<GetMessagingOptOutsQuery, PaginatedList<MessagingOptOutDto>>
{
    private readonly IApplicationDbContext _context;

    public GetMessagingOptOutsQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<PaginatedList<MessagingOptOutDto>> Handle(GetMessagingOptOutsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.MessagingOptOuts.AsNoTracking();
        if (!request.IncludeResubscribed)
            query = query.Where(o => o.ResubscribedAt == null);
        var digits = new string((request.Search ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length > 0)
            query = query.Where(o => o.Phone.Contains(digits));

        var page = await PaginatedList<MessagingOptOut>.CreateAsync(
            query.OrderByDescending(o => o.OptedOutAt), request.PageNumber, request.PageSize, cancellationToken);
        var (pageNumber, pageSize) = Paging.Normalize(request.PageNumber, request.PageSize);
        return new PaginatedList<MessagingOptOutDto>(page.Items.Select(o => o.ToDto()).ToList(), page.TotalCount, pageNumber, pageSize);
    }
}

/// <summary>Records, at the customer's request, that they do or do not want campaigns and reminders.</summary>
public record SetMessagingOptOutCommand : IRequest<Result>
{
    public string Phone { get; init; } = string.Empty;
    public bool OptedOut { get; init; } = true;
}

public class SetMessagingOptOutCommandValidator : AbstractValidator<SetMessagingOptOutCommand>
{
    public SetMessagingOptOutCommandValidator()
    {
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30);
    }
}

public class SetMessagingOptOutCommandHandler : IRequestHandler<SetMessagingOptOutCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly MessagingConsentService _consent;
    private readonly IPhoneNormalizationService _phones;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public SetMessagingOptOutCommandHandler(
        IApplicationDbContext context,
        MessagingConsentService consent,
        IPhoneNormalizationService phones,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser,
        IDateTimeProvider clock)
    {
        _context = context;
        _consent = consent;
        _phones = phones;
        _tenant = tenant;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(SetMessagingOptOutCommand request, CancellationToken cancellationToken)
    {
        if (!_phones.TryNormalizeToE164(request.Phone, "MX", out var phone))
            return Result.Failure("El teléfono no es válido.");

        var changed = await _consent.SetAsync(_tenant.TenantId, phone, request.OptedOut, OptOutSource.Staff, cancellationToken);
        if (!changed)
            return Result.Success();

        CampaignRules.Audit(_context, _currentUser, _tenant.TenantId, nameof(MessagingOptOut), Guid.Empty,
            request.OptedOut ? "MessagingOptOut" : "MessagingOptIn",
            new { Phone = CampaignRules.MaskPhone(MessagingPhone.ToKey(phone)), Source = OptOutSource.Staff }, _clock.UtcNow);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The customer wrote BAJA at the same moment: their choice is already stored.
            return Result.Success();
        }

        return Result.Success();
    }
}
