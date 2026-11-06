using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Notices;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;

namespace FoodParkDigital.Application.Appointments.RequestNotices;

/// <summary>
/// Whether the person gets the agenda's emails of the business they are in: one with each request online (Fase 29N, ADR 0068)
/// and, since the Fase 29O (ADR 0069), one with each appointment its customer cancels.
/// </summary>
public class RequestEmailsDto
{
    /// <summary>They get them: as they chose or, until they choose, because they own the business.</summary>
    public bool Enabled { get; set; }

    /// <summary>Where they arrive: the email of their account.</summary>
    public string? Email { get; set; }

    /// <summary>That email is confirmed: until it is, nothing is sent to it.</summary>
    public bool EmailConfirmed { get; set; }
}

/// <summary>The person's own choice, for whoever answers the requests (the agenda's permission).</summary>
[RequiresPermission(TenantPermission.ManageAppointments)]
[RequiresCapability(TenantCapability.Appointments)]
public record GetRequestEmailsQuery : IRequest<Result<RequestEmailsDto>>;

/// <summary>
/// Turns them on or off for the person who calls, in this business only. Someone else's are chosen from the team, by whoever
/// runs it (Fase 29Q, ADR 0071).
/// </summary>
[RequiresPermission(TenantPermission.ManageAppointments)]
[RequiresCapability(TenantCapability.Appointments)]
public record UpdateRequestEmailsCommand(bool Enabled) : IRequest<Result<RequestEmailsDto>>;

public class RequestEmailsHandler :
    IRequestHandler<GetRequestEmailsQuery, Result<RequestEmailsDto>>,
    IRequestHandler<UpdateRequestEmailsCommand, Result<RequestEmailsDto>>
{
    public const string NotAMemberMessage = NoticeEmailPreferences.NotAMemberMessage;

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUser;

    public RequestEmailsHandler(IApplicationDbContext context, ICurrentTenantService tenantService, ICurrentUserService currentUser)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUser = currentUser;
    }

    public async Task<Result<RequestEmailsDto>> Handle(GetRequestEmailsQuery request, CancellationToken cancellationToken)
    {
        var member = await NoticeEmailPreferences.MemberAsync(_context, _tenantService, _currentUser, tracked: false, cancellationToken);
        return member is null
            ? Result.Failure<RequestEmailsDto>(NotAMemberMessage)
            : Result.Success(await DescribeAsync(member, cancellationToken));
    }

    public async Task<Result<RequestEmailsDto>> Handle(UpdateRequestEmailsCommand request, CancellationToken cancellationToken)
    {
        var member = await NoticeEmailPreferences.MemberAsync(_context, _tenantService, _currentUser, tracked: true, cancellationToken);
        if (member is null)
            return Result.Failure<RequestEmailsDto>(NotAMemberMessage);

        member.ChooseRequestEmails(request.Enabled);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(await DescribeAsync(member, cancellationToken));
    }

    private async Task<RequestEmailsDto> DescribeAsync(TenantUser member, CancellationToken cancellationToken)
    {
        var (email, confirmed) = await NoticeEmailPreferences.AccountAsync(_context, member, cancellationToken);
        return new RequestEmailsDto { Enabled = member.GetsRequestEmails, Email = email, EmailConfirmed = confirmed };
    }
}
