using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Domain.Customers.Entities;
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
            .WithMessage("El codigo debe tener 5 o 6 digitos.");
    }
}

public class ConfirmPhoneCommandHandler : IRequestHandler<ConfirmPhoneCommand, Result>
{
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

            if (string.IsNullOrEmpty(user.PhoneNumber))
                return Result.Failure("El usuario no tiene un numero de telefono registrado.");

            var normalizedCode = NormalizeVerificationCode(request.Code);
            if (normalizedCode is null)
                return Result.Failure("Codigo invalido o expirado.");

            var result = await _userManager.ChangePhoneNumberAsync(user, user.PhoneNumber, normalizedCode);
            if (!result.Succeeded)
                return Result.Failure("Codigo invalido o expirado.");

            if (user.Role == UserRole.Customer)
            {
                await _claimService.ClaimByPhoneAsync(user.PhoneNumber, user.Id, "MX", ct);
                await AssignPhoneToCustomerProfileAsync(user, user.PhoneNumber, ct);
            }
            else
            {
                await EnsureTenantContextForUser(user, ct);
            }

            return Result.Success();
        }, cancellationToken);
    }

    /// <summary>
    /// The user just proved the number is theirs, so their profile takes it and orders placed with it
    /// are linked to them from now on. The number is taken from a profile whose account never
    /// confirmed it, never from an account that confirmed it first.
    /// </summary>
    private async Task AssignPhoneToCustomerProfileAsync(
        ApplicationUser user,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        var phoneNormalized = _phoneNormalizationService.NormalizeToE164(
            phoneNumber,
            CustomerPhoneOwnershipRules.DefaultCountry);
        var holder = await CustomerPhoneOwnershipRules.FindHolderAsync(
            _context,
            _phoneNormalizationService,
            phoneNormalized,
            cancellationToken);

        if (holder is not null && holder.UserId != user.Id)
        {
            if (holder.IsVerified)
                return;

            // The phone is unique across profiles, so the unverified one must release it first.
            // Its account is kept; it just stops holding a number it never proved.
            var unverifiedProfile = await _context.CustomerProfiles
                .FirstOrDefaultAsync(x => x.UserId == holder.UserId, cancellationToken);
            if (unverifiedProfile is not null)
            {
                _context.CustomerProfiles.Remove(unverifiedProfile);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        var profile = await _context.CustomerProfiles
            .FirstOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);

        if (profile is null)
        {
            _context.CustomerProfiles.Add(new CustomerProfile(
                user.Id,
                phoneNormalized,
                $"{user.FirstName} {user.LastName}".Trim(),
                user.FirstName,
                user.LastName));
        }
        else if (profile.PhoneNormalized != phoneNormalized)
        {
            // The account confirmed a different number, so its previous one stops identifying it.
            profile.UpdateProfile(phoneNormalized, profile.DisplayName, profile.FirstName, profile.LastName);
        }
        else
        {
            return;
        }

        await _context.SaveChangesAsync(cancellationToken);
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

    private static string? NormalizeVerificationCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var digits = new string(code.Where(char.IsDigit).ToArray());
        if (digits.Length == 5)
            return digits.PadLeft(6, '0');

        return digits.Length == 6 ? digits : null;
    }
}
