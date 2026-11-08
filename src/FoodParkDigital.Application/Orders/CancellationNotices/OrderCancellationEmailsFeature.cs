using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Notices;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;

namespace FoodParkDigital.Application.Orders.CancellationNotices;

/// <summary>
/// Whether the person gets an email each time a customer cancels an online order of the business they are in (Fase 30B, ADR
/// 0082).
/// </summary>
public class OrderCancellationEmailsDto
{
    /// <summary>They get them: as they chose or, until they choose, because they own the business.</summary>
    public bool Enabled { get; set; }

    /// <summary>Where they arrive: the email of their account.</summary>
    public string? Email { get; set; }

    /// <summary>That email is confirmed: until it is, nothing is sent to it.</summary>
    public bool EmailConfirmed { get; set; }
}

/// <summary>The person's own choice, for whoever confirms the orders (what the orders' API asks).</summary>
[RequiresPermission(TenantPermission.ManageOrders)]
[RequiresCapability(OrderCancellationEmail.Modules)]
public record GetOrderCancellationEmailsQuery : IRequest<Result<OrderCancellationEmailsDto>>;

/// <summary>
/// Turns them on or off for the person who calls, in this business only. Someone else's are chosen from the team, by whoever
/// runs it (Fase 29Q, ADR 0071).
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
[RequiresCapability(OrderCancellationEmail.Modules)]
public record UpdateOrderCancellationEmailsCommand(bool Enabled) : IRequest<Result<OrderCancellationEmailsDto>>;

public class OrderCancellationEmailsHandler :
    IRequestHandler<GetOrderCancellationEmailsQuery, Result<OrderCancellationEmailsDto>>,
    IRequestHandler<UpdateOrderCancellationEmailsCommand, Result<OrderCancellationEmailsDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUser;

    public OrderCancellationEmailsHandler(IApplicationDbContext context, ICurrentTenantService tenantService, ICurrentUserService currentUser)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUser = currentUser;
    }

    public async Task<Result<OrderCancellationEmailsDto>> Handle(GetOrderCancellationEmailsQuery request, CancellationToken cancellationToken)
    {
        var member = await NoticeEmailPreferences.MemberAsync(_context, _tenantService, _currentUser, tracked: false, cancellationToken);
        return member is null
            ? Result.Failure<OrderCancellationEmailsDto>(NoticeEmailPreferences.NotAMemberMessage)
            : Result.Success(await DescribeAsync(member, cancellationToken));
    }

    public async Task<Result<OrderCancellationEmailsDto>> Handle(UpdateOrderCancellationEmailsCommand request, CancellationToken cancellationToken)
    {
        var member = await NoticeEmailPreferences.MemberAsync(_context, _tenantService, _currentUser, tracked: true, cancellationToken);
        if (member is null)
            return Result.Failure<OrderCancellationEmailsDto>(NoticeEmailPreferences.NotAMemberMessage);

        member.ChooseOrderCancellationEmails(request.Enabled);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(await DescribeAsync(member, cancellationToken));
    }

    private async Task<OrderCancellationEmailsDto> DescribeAsync(TenantUser member, CancellationToken cancellationToken)
    {
        var (email, confirmed) = await NoticeEmailPreferences.AccountAsync(_context, member, cancellationToken);
        return new OrderCancellationEmailsDto { Enabled = member.GetsOrderCancellationEmails, Email = email, EmailConfirmed = confirmed };
    }
}
