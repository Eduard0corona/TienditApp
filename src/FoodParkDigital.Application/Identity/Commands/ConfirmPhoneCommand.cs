using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Commands;

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

    public ConfirmPhoneCommandHandler(
        UserManager<ApplicationUser> userManager,
        IApplicationDbContext context,
        ITemporaryCustomerClaimService claimService)
    {
        _userManager = userManager;
        _context = context;
        _claimService = claimService;
    }

    public async Task<Result> Handle(ConfirmPhoneCommand request, CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id.ToString() == request.UserId, cancellationToken);
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
            await _claimService.ClaimByPhoneAsync(user.PhoneNumber, user.Id, "MX", cancellationToken);
        else
            await EnsureTenantContextForUser(user, cancellationToken);

        return Result.Success();
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
