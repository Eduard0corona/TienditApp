using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Notices;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;

namespace FoodParkDigital.Application.Quotes.ReplyNotices;

/// <summary>
/// Whether the person gets an email each time a customer accepts or rejects a quote of the business they are in from its link
/// (Fase 29O, ADR 0069).
/// </summary>
public class QuoteReplyEmailsDto
{
    /// <summary>They get them: as they chose or, until they choose, because they own the business.</summary>
    public bool Enabled { get; set; }

    /// <summary>Where they arrive: the email of their account.</summary>
    public string? Email { get; set; }

    /// <summary>That email is confirmed: until it is, nothing is sent to it.</summary>
    public bool EmailConfirmed { get; set; }
}

/// <summary>The person's own choice, for whoever works the quotes (what the quotes' API asks).</summary>
[RequiresPermission(TenantPermission.ManageOrders)]
[RequiresCapability(QuoteReplyEmail.Modules)]
public record GetQuoteReplyEmailsQuery : IRequest<Result<QuoteReplyEmailsDto>>;

/// <summary>
/// Turns them on or off for the person who calls, in this business only. Someone else's are chosen from the team, by whoever
/// runs it (Fase 29Q, ADR 0071).
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
[RequiresCapability(QuoteReplyEmail.Modules)]
public record UpdateQuoteReplyEmailsCommand(bool Enabled) : IRequest<Result<QuoteReplyEmailsDto>>;

public class QuoteReplyEmailsHandler :
    IRequestHandler<GetQuoteReplyEmailsQuery, Result<QuoteReplyEmailsDto>>,
    IRequestHandler<UpdateQuoteReplyEmailsCommand, Result<QuoteReplyEmailsDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUser;

    public QuoteReplyEmailsHandler(IApplicationDbContext context, ICurrentTenantService tenantService, ICurrentUserService currentUser)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUser = currentUser;
    }

    public async Task<Result<QuoteReplyEmailsDto>> Handle(GetQuoteReplyEmailsQuery request, CancellationToken cancellationToken)
    {
        var member = await NoticeEmailPreferences.MemberAsync(_context, _tenantService, _currentUser, tracked: false, cancellationToken);
        return member is null
            ? Result.Failure<QuoteReplyEmailsDto>(NoticeEmailPreferences.NotAMemberMessage)
            : Result.Success(await DescribeAsync(member, cancellationToken));
    }

    public async Task<Result<QuoteReplyEmailsDto>> Handle(UpdateQuoteReplyEmailsCommand request, CancellationToken cancellationToken)
    {
        var member = await NoticeEmailPreferences.MemberAsync(_context, _tenantService, _currentUser, tracked: true, cancellationToken);
        if (member is null)
            return Result.Failure<QuoteReplyEmailsDto>(NoticeEmailPreferences.NotAMemberMessage);

        member.ChooseQuoteReplyEmails(request.Enabled);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(await DescribeAsync(member, cancellationToken));
    }

    private async Task<QuoteReplyEmailsDto> DescribeAsync(TenantUser member, CancellationToken cancellationToken)
    {
        var (email, confirmed) = await NoticeEmailPreferences.AccountAsync(_context, member, cancellationToken);
        return new QuoteReplyEmailsDto { Enabled = member.GetsQuoteReplyEmails, Email = email, EmailConfirmed = confirmed };
    }
}
