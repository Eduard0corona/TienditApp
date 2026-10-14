using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Employees.DTOs;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Employees.Commands;

/// <summary>A new invitation link for someone who has not accepted theirs (lost, expired or never received).</summary>
[RequiresTenantManager]
public record ResendEmployeeInvitationCommand(Guid TenantUserId) : IRequest<Result<EmployeeInvitationDto>>;

public class ResendEmployeeInvitationCommandHandler : IRequestHandler<ResendEmployeeInvitationCommand, Result<EmployeeInvitationDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IOutbox _outbox;

    public ResendEmployeeInvitationCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        UserManager<ApplicationUser> userManager,
        IOutbox outbox)
    {
        _context = context;
        _tenantService = tenantService;
        _userManager = userManager;
        _outbox = outbox;
    }

    public async Task<Result<EmployeeInvitationDto>> Handle(ResendEmployeeInvitationCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        var membership = await _context.TenantUsers
            .FirstOrDefaultAsync(x => x.Id == request.TenantUserId && x.TenantId == tenantId, cancellationToken);
        if (membership is null)
            return Result.Failure<EmployeeInvitationDto>("Empleado no encontrado.");
        if (membership.Status != TenantUserStatus.Invited)
            return Result.Failure<EmployeeInvitationDto>("Esta persona ya no tiene una invitación pendiente.");

        // The account is shared by the businesses of the person; this one only reaches it through its membership.
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == membership.UserId, cancellationToken);
        if (user is null)
            return Result.Failure<EmployeeInvitationDto>("Empleado no encontrado.");
        if (await _userManager.HasPasswordAsync(user))
            return Result.Failure<EmployeeInvitationDto>("Ya eligió su contraseña: actívalo desde la lista.");

        var path = await EmployeeInvitations.IssueAsync(_userManager, _outbox, user, tenantId);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(new EmployeeInvitationDto { InvitationUrl = path, Email = user.Email ?? string.Empty });
    }
}
