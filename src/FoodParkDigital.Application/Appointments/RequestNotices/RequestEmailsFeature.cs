using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments.RequestNotices;

/// <summary>Whether the person gets an email with each request online of the business they are in (Fase 29N, ADR 0068).</summary>
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

/// <summary>Turns them on or off for the person, in this business only. Nobody chooses for someone else.</summary>
[RequiresPermission(TenantPermission.ManageAppointments)]
[RequiresCapability(TenantCapability.Appointments)]
public record UpdateRequestEmailsCommand(bool Enabled) : IRequest<Result<RequestEmailsDto>>;

public class RequestEmailsHandler :
    IRequestHandler<GetRequestEmailsQuery, Result<RequestEmailsDto>>,
    IRequestHandler<UpdateRequestEmailsCommand, Result<RequestEmailsDto>>
{
    public const string NotAMemberMessage = "Estos avisos son para el equipo del negocio.";

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
        var member = await MemberAsync(tracked: false, cancellationToken);
        return member is null
            ? Result.Failure<RequestEmailsDto>(NotAMemberMessage)
            : Result.Success(await DescribeAsync(member, cancellationToken));
    }

    public async Task<Result<RequestEmailsDto>> Handle(UpdateRequestEmailsCommand request, CancellationToken cancellationToken)
    {
        var member = await MemberAsync(tracked: true, cancellationToken);
        if (member is null)
            return Result.Failure<RequestEmailsDto>(NotAMemberMessage);

        member.ChooseRequestEmails(request.Enabled);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(await DescribeAsync(member, cancellationToken));
    }

    /// <summary>The person's membership in the current business; none for whoever is not on its team (the platform's admin).</summary>
    private Task<TenantUser?> MemberAsync(bool tracked, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return Task.FromResult<TenantUser?>(null);

        var members = tracked ? _context.TenantUsers : _context.TenantUsers.AsNoTracking();
        return members.FirstOrDefaultAsync(m => m.TenantId == _tenantService.TenantId && m.UserId == userId, cancellationToken);
    }

    private async Task<RequestEmailsDto> DescribeAsync(TenantUser member, CancellationToken cancellationToken)
    {
        var account = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == member.UserId)
            .Select(u => new { u.Email, u.EmailConfirmed })
            .FirstAsync(cancellationToken);
        return new RequestEmailsDto
        {
            Enabled = member.GetsRequestEmails,
            Email = account.Email,
            EmailConfirmed = account.EmailConfirmed
        };
    }
}
