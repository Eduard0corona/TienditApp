using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Domain.Identity.Entities;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;

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
        var user = await _userManager.FindByEmailAsync(request.Email);
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

        var accessToken = _jwtTokenService.GenerateAccessToken(user);
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
                TenantId = user.TenantId,
                IsActive = user.IsActive
            }
        });
    }
}
