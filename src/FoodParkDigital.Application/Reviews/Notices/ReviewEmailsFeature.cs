using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Notices;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Tenants.Entities;
using MediatR;

namespace FoodParkDigital.Application.Reviews.Notices;

/// <summary>Whether the person gets an email each time a customer writes a new review of the business they are in (Fase 31K, ADR 0091).</summary>
public class ReviewEmailsDto
{
    /// <summary>They get them: as they chose or, until they choose, because they own the business.</summary>
    public bool Enabled { get; set; }

    /// <summary>Where they arrive: the email of their account.</summary>
    public string? Email { get; set; }

    /// <summary>That email is confirmed: until it is, nothing is sent to it.</summary>
    public bool EmailConfirmed { get; set; }
}

/// <summary>The person's own choice, for whoever reads and answers the reviews (what the reviews' API asks).</summary>
[RequiresTenantManager]
public record GetReviewEmailsQuery : IRequest<Result<ReviewEmailsDto>>;

/// <summary>
/// Turns them on or off for the person who calls, in this business only. Someone else's are chosen from the team, by whoever
/// runs it (Fase 29Q, ADR 0071).
/// </summary>
[RequiresTenantManager]
public record UpdateReviewEmailsCommand(bool Enabled) : IRequest<Result<ReviewEmailsDto>>;

public class ReviewEmailsHandler :
    IRequestHandler<GetReviewEmailsQuery, Result<ReviewEmailsDto>>,
    IRequestHandler<UpdateReviewEmailsCommand, Result<ReviewEmailsDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUser;

    public ReviewEmailsHandler(IApplicationDbContext context, ICurrentTenantService tenantService, ICurrentUserService currentUser)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUser = currentUser;
    }

    public async Task<Result<ReviewEmailsDto>> Handle(GetReviewEmailsQuery request, CancellationToken cancellationToken)
    {
        var member = await NoticeEmailPreferences.MemberAsync(_context, _tenantService, _currentUser, tracked: false, cancellationToken);
        return member is null
            ? Result.Failure<ReviewEmailsDto>(NoticeEmailPreferences.NotAMemberMessage)
            : Result.Success(await DescribeAsync(member, cancellationToken));
    }

    public async Task<Result<ReviewEmailsDto>> Handle(UpdateReviewEmailsCommand request, CancellationToken cancellationToken)
    {
        var member = await NoticeEmailPreferences.MemberAsync(_context, _tenantService, _currentUser, tracked: true, cancellationToken);
        if (member is null)
            return Result.Failure<ReviewEmailsDto>(NoticeEmailPreferences.NotAMemberMessage);

        member.ChooseReviewEmails(request.Enabled);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(await DescribeAsync(member, cancellationToken));
    }

    private async Task<ReviewEmailsDto> DescribeAsync(TenantUser member, CancellationToken cancellationToken)
    {
        var (email, confirmed) = await NoticeEmailPreferences.AccountAsync(_context, member, cancellationToken);
        return new ReviewEmailsDto { Enabled = member.GetsReviewEmails, Email = email, EmailConfirmed = confirmed };
    }
}
