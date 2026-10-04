using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Identity;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Commands;

[AllowAnonymousRequest("Acceso público: inicio de sesión, registro y confirmaciones.")]
public record ConfirmPhoneCommand : IRequest<Result>
{
    public string UserId { get; init; } = null!;
    public string Code { get; init; } = null!;
}

public class ConfirmPhoneCommandValidator : AbstractValidator<ConfirmPhoneCommand>
{
    public ConfirmPhoneCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Code)
            .NotEmpty()
            .Matches(@"^\d{5,6}$")
            .WithMessage("El código debe tener 5 o 6 dígitos.");
    }
}

public class ConfirmPhoneCommandHandler : IRequestHandler<ConfirmPhoneCommand, Result>
{
    public const string ConfirmFromProfileMessage = "Inicia sesión y confirma tu teléfono desde tu perfil.";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApplicationDbContext _context;
    private readonly ITemporaryCustomerClaimService _claimService;
    private readonly IPhoneNormalizationService _phoneNormalizationService;

    public ConfirmPhoneCommandHandler(
        UserManager<ApplicationUser> userManager,
        IApplicationDbContext context,
        ITemporaryCustomerClaimService claimService,
        IPhoneNormalizationService phoneNormalizationService)
    {
        _userManager = userManager;
        _context = context;
        _claimService = claimService;
        _phoneNormalizationService = phoneNormalizationService;
    }

    public async Task<Result> Handle(ConfirmPhoneCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
            return Result.Failure("Usuario no encontrado.");

        // The confirmation and the follow-up linking commit together: a confirmed phone with a failed
        // link would make every retry return early without linking. The user is loaded inside the
        // unit so a retried attempt verifies the code against committed data.
        return await _context.ExecuteInResilientTransactionAsync(async ct =>
        {
            var user = await _context.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null)
                return Result.Failure("Usuario no encontrado.");

            if (user.PhoneNumberConfirmed)
                return Result.Success();

            // Only an account being registered confirms its phone here, before it can sign in. Once it signs in (or
            // while an invitation waits to be accepted) the number is confirmed from the profile, with the password:
            // whoever typed it must not take over the account by recovering its password by SMS.
            if (user.EmailConfirmed || user.PasswordHash is null)
                return Result.Failure(ConfirmFromProfileMessage);

            if (string.IsNullOrEmpty(user.PhoneNumber))
                return Result.Failure("El usuario no tiene un número de teléfono registrado.");

            var normalizedCode = AccountSecurity.NormalizeCode(request.Code);
            if (normalizedCode is null)
                return Result.Failure("Codigo invalido o expirado.");

            var result = await _userManager.ChangePhoneNumberAsync(user, user.PhoneNumber, normalizedCode);
            if (!result.Succeeded)
                return Result.Failure("Codigo invalido o expirado.");

            if (user.Role == UserRole.Customer)
            {
                await _claimService.ClaimByPhoneAsync(user.PhoneNumber, user.Id, "MX", ct);
                await CustomerPhoneOwnershipRules.AssignConfirmedPhoneAsync(_context, _phoneNormalizationService, user, user.PhoneNumber, ct);
            }
            else
            {
                await EnsureTenantContextForUser(user, ct);
            }

            return Result.Success();
        }, cancellationToken);
    }

    private async Task EnsureTenantContextForUser(ApplicationUser user, CancellationToken cancellationToken)
    {
        var membership = await _context.TenantUsers
            .IgnoreQueryFilters()
            .Where(x => x.UserId == user.Id && x.Status == TenantUserStatus.Active)
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (membership is null && user.DefaultTenantId.HasValue)
        {
            var role = user.Role == UserRole.TenantAdmin
                ? TenantUserRole.Admin
                : TenantUserRole.Staff;

            membership = new TenantUser(user.DefaultTenantId.Value, user.Id, role, TenantUserStatus.Active);
            _context.TenantUsers.Add(membership);
        }

        if (!user.DefaultTenantId.HasValue && membership is not null)
            user.DefaultTenantId = membership.TenantId;

        await _context.SaveChangesAsync(cancellationToken);
    }
}
