using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Domain.Identity.Entities;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Commands;

public record LoginCommand : IRequest<Result<AuthResponse>>
{
    public string Email { get; init; } = null!;
    public string Password { get; init; } = null!;
}

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public class LoginCommandHandler : IRequestHandler<LoginCommand, Result<AuthResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISignInService _signInService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IApplicationDbContext _context;

    public LoginCommandHandler(
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

    public async Task<Result<AuthResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive)
            return Result.Failure<AuthResponse>("Credenciales inválidas.");

        var signInResult = await _signInService.CheckPasswordSignInAsync(user.Id, request.Password, false);
        if (!signInResult.Succeeded)
        {
            if (signInResult.IsLockedOut)
                return Result.Failure<AuthResponse>("La cuenta está bloqueada. Intente más tarde.");

            return Result.Failure<AuthResponse>("Credenciales inválidas.");
        }

        // Verificar si el usuario tiene 2FA habilitado
        var is2FAEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
        if (is2FAEnabled)
        {
            return Result.Success(new AuthResponse
            {
                RequiresTwoFactor = true,
                UserId = user.Id.ToString()
            });
        }

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
