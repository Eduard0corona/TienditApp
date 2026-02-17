using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Commands.TwoFactor;

public record Login2FACommand : IRequest<Result<AuthResponse>>
{
    public string Email { get; init; } = null!;
    public string Code { get; init; } = null!;
    public bool UseRecoveryCode { get; init; }
}

public class Login2FACommandValidator : AbstractValidator<Login2FACommand>
{
    public Login2FACommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Code).NotEmpty();
    }
}

public class Login2FACommandHandler : IRequestHandler<Login2FACommand, Result<AuthResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISignInService _signInService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IApplicationDbContext _context;

    public Login2FACommandHandler(
        UserManager<ApplicationUser> userManager,
        ISignInService signInService,
        IJwtTokenService jwtTokenService,
        IApplicationDbContext context)
    {
        _userManager = userManager;
        _signInService = signInService;
        _jwtTokenService = jwtTokenService;
        _context = context;
    }

    public async Task<Result<AuthResponse>> Handle(Login2FACommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.ToUpperInvariant();
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
        if (user is null || !user.IsActive)
            return Result.Failure<AuthResponse>("Credenciales inválidas.");

        SignInResultDto result;

        if (request.UseRecoveryCode)
        {
            var recoveryCode = request.Code.Replace(" ", string.Empty);
            result = await _signInService.TwoFactorRecoveryCodeSignInAsync(recoveryCode);
        }
        else
        {
            var verificationCode = request.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
            result = await _signInService.TwoFactorAuthenticatorSignInAsync(verificationCode, false, false);
        }

        if (!result.Succeeded)
            return Result.Failure<AuthResponse>("Código de autenticación inválido.");

        var tenantMembership = await _context.TenantUsers
            .IgnoreQueryFilters()
            .Where(x => x.UserId == user.Id && x.Status == TenantUserStatus.Active)
            .OrderByDescending(x => x.TenantId == user.DefaultTenantId)
            .ThenBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (tenantMembership is null && user.DefaultTenantId.HasValue)
        {
            var role = user.Role == UserRole.TenantAdmin
                ? TenantUserRole.Admin
                : TenantUserRole.Staff;

            tenantMembership = new TenantUser(user.DefaultTenantId.Value, user.Id, role, TenantUserStatus.Active);
            _context.TenantUsers.Add(tenantMembership);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var tenantContext = tenantMembership is null
            ? null
            : new TenantTokenContext(tenantMembership.TenantId, tenantMembership.Role.ToString().ToUpperInvariant());

        if (user.DefaultTenantId is null && tenantMembership is not null)
            user.DefaultTenantId = tenantMembership.TenantId;

        var accessToken = _jwtTokenService.GenerateAccessToken(user, tenantContext);
        var refreshToken = _jwtTokenService.GenerateRefreshToken(user.Id);

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(60),
            RequiresTwoFactor = false,
            User = new UserDto
            {
                Id = user.Id,
                Email = user.Email!,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Role = user.Role.ToString(),
                TenantId = tenantMembership?.TenantId ?? user.DefaultTenantId,
                IsActive = user.IsActive
            }
        });
    }
}
