using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Employees.DTOs;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Domain.Tenants.Events;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Employees.Commands;

/// <summary>
/// Changes the role and permissions of a person in this business. Their account is theirs: the phone only changes from
/// their profile, with a code (it recovers the password by SMS), and the name only here while nobody else shares it.
/// </summary>
[RequiresTenantManager]
public record UpdateEmployeeCommand : IRequest<Result<EmployeeDto>>
{
    public Guid TenantUserId { get; init; }
    public string FirstName { get; init; } = null!;
    public string LastName { get; init; } = null!;
    public UserRole AppRole { get; init; } = UserRole.Staff;
    public TenantUserRole TenantRole { get; init; } = TenantUserRole.Staff;
    /// <summary>Null takes the defaults of the role; an empty list is "sin acceso".</summary>
    public List<string>? Permissions { get; init; }
    /// <summary>Whether they get the agenda's emails (Fase 29Q, ADR 0071); null leaves what they have.</summary>
    public bool? RequestEmails { get; init; }
    /// <summary>Whether they get an email with each answer to a quote from its link; null leaves what they have.</summary>
    public bool? QuoteReplyEmails { get; init; }
    /// <summary>
    /// Whether they get an email with each online order its customer cancels (Fase 30B, ADR 0082), and the reminder of the one
    /// still waiting to be accepted (Fase 31K); null leaves what they have.
    /// </summary>
    public bool? OrderCancellationEmails { get; init; }
    /// <summary>
    /// Whether they get an email with each new review (Fase 31K, ADR 0091), which only owners and admins answer; null leaves
    /// what they have.
    /// </summary>
    public bool? ReviewEmails { get; init; }
}

public class UpdateEmployeeCommandValidator : AbstractValidator<UpdateEmployeeCommand>
{
    public UpdateEmployeeCommandValidator()
    {
        RuleFor(x => x.TenantUserId).NotEmpty();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AppRole)
            .Must(role => role is UserRole.TenantAdmin or UserRole.Staff or UserRole.Kitchen)
            .WithMessage("El rol de aplicación no es válido para empleados.");
        RuleFor(x => x.TenantRole)
            .Must(role => role is TenantUserRole.Admin or TenantUserRole.Staff)
            .WithMessage("El rol en el negocio no es válido para empleados.");
    }
}

public class UpdateEmployeeCommandHandler : IRequestHandler<UpdateEmployeeCommand, Result<EmployeeDto>>
{
    public const string SharedAccountNameMessage =
        "Esta persona usa su cuenta también en otro negocio: solo ella puede cambiar su nombre, desde su perfil.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IOutbox _outbox;

    public UpdateEmployeeCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService,
        UserManager<ApplicationUser> userManager,
        IOutbox outbox)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
        _userManager = userManager;
        _outbox = outbox;
    }

    public async Task<Result<EmployeeDto>> Handle(UpdateEmployeeCommand request, CancellationToken cancellationToken)
    {
        var tenantUser = await _context.TenantUsers
            .FirstOrDefaultAsync(
                x => x.Id == request.TenantUserId && x.TenantId == _tenantService.TenantId,
                cancellationToken);

        if (tenantUser is null)
            return Result.Failure<EmployeeDto>("Empleado no encontrado.");

        var user = await _userManager.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == tenantUser.UserId, cancellationToken);

        if (user is null || user.Role == UserRole.Customer)
            return Result.Failure<EmployeeDto>("Empleado no encontrado.");

        // Before anything changes: a refused change of the owner's notices leaves their name and permissions as they were.
        var asked = new NoticeChoices(
            request.RequestEmails, request.QuoteReplyEmails, request.OrderCancellationEmails, request.ReviewEmails);
        if (EmployeeNoticeEmails.RefusalFor(tenantUser, asked) is { } noticesRefused)
            return Result.Failure<EmployeeDto>(noticesRefused);

        // The owner keeps the business: their role never changes from here.
        var isOwner = tenantUser.Role == TenantUserRole.Owner;
        var appRole = isOwner ? user.Role : request.AppRole;
        var tenantRole = isOwner
            ? TenantUserRole.Owner
            : request.AppRole == UserRole.TenantAdmin ? TenantUserRole.Admin : request.TenantRole;

        if (EmployeeAccessRules.IsManagerRole(tenantUser.Role) && !EmployeeAccessRules.IsManagerRole(tenantRole))
        {
            if (Guid.TryParse(_currentUserService.UserId, out var currentUserId) && currentUserId == tenantUser.UserId)
                return Result.Failure<EmployeeDto>(EmployeeAccessRules.SelfDemotionMessage);
            if (!await EmployeeAccessRules.HasAnotherActiveManagerAsync(_context, _tenantService.TenantId, tenantUser.Id, cancellationToken))
                return Result.Failure<EmployeeDto>(EmployeeAccessRules.LastManagerMessage);
        }

        // The account is shared by every business of the person: with another business, only this membership changes
        // (the role inside each business comes from its membership) and the name is theirs to change.
        var belongsToAnotherBusiness = await _context.TenantUsers
            .IgnoreQueryFilters()
            .AnyAsync(x => x.UserId == user.Id && x.TenantId != _tenantService.TenantId, cancellationToken);

        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();
        if (firstName != user.FirstName || lastName != user.LastName)
        {
            if (belongsToAnotherBusiness)
                return Result.Failure<EmployeeDto>(SharedAccountNameMessage);

            user.FirstName = firstName;
            user.LastName = lastName;
        }

        if (user.Role != appRole && !belongsToAnotherBusiness)
        {
            user.Role = appRole;
            var currentRoles = await _userManager.GetRolesAsync(user);
            if (currentRoles.Count > 0)
            {
                var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
                if (!removeResult.Succeeded)
                {
                    var removeErrors = string.Join(", ", removeResult.Errors.Select(e => e.Description));
                    return Result.Failure<EmployeeDto>(removeErrors);
                }
            }

            var addRoleResult = await _userManager.AddToRoleAsync(user, EmployeeDefinitions.ToIdentityRole(appRole));
            if (!addRoleResult.Succeeded)
            {
                var addErrors = string.Join(", ", addRoleResult.Errors.Select(e => e.Description));
                return Result.Failure<EmployeeDto>(addErrors);
            }
        }

        tenantUser.SetRole(tenantRole);
        if (request.Permissions is null)
            tenantUser.SetPermissionCodes(TenantPermissionCatalog.GetDefaultCodes(appRole, tenantRole));
        else
            tenantUser.SetExplicitPermissions(request.Permissions);
        var changed = EmployeeNoticeEmails.Apply(tenantUser, asked);
        // Fase 30A (ADR 0081): someone else changed what the person gets by email; it is told who and what, saved with the change.
        if (changed.Any && Guid.TryParse(_currentUserService.UserId, out var changedBy) && changedBy != tenantUser.UserId)
            _outbox.Add(
                new TenantUserNoticeEmailsChangedEvent(
                    tenantUser.Id, changedBy, changed.RequestEmails, changed.QuoteReplyEmails, changed.OrderCancellationEmails,
                    changed.ReviewEmails),
                _tenantService.TenantId);

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new EmployeeDto
        {
            TenantUserId = tenantUser.Id,
            UserId = user.Id,
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName,
            LastName = user.LastName,
            PhoneNumber = user.PhoneNumber,
            AppRole = user.Role.ToString(),
            TenantRole = tenantUser.Role.ToString(),
            Status = tenantUser.Status.ToString(),
            IsActive = tenantUser.Status == TenantUserStatus.Active && user.IsActive,
            CreatedAt = tenantUser.CreatedAt,
            Permissions = TenantPermissionCatalog.ResolveEffectiveCodes(tenantUser.PermissionCodes, user.Role, tenantUser.Role).ToList(),
            NoticeEmails = EmployeeNoticeEmailsDto.From(tenantUser, user.EmailConfirmed)
        });
    }
}
