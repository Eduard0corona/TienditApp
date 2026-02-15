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
        // IgnoreQueryFilters: login happens before tenant is resolved (no JWT yet)
        var normalizedEmail = request.Email.ToUpperInvariant();
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

        if (user is null || !user.IsActive)
            return Result.Failure<AuthResponse>("Credenciales inválidas.");

        if (!user.EmailConfirmed && !user.PhoneNumberConfirmed)
            return Result.Failure<AuthResponse>("Debe confirmar su cuenta por email o SMS antes de iniciar sesión.");

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
                PhoneNumber = user.PhoneNumber,
                PhoneNumberConfirmed = user.PhoneNumberConfirmed,
                Role = user.Role.ToString(),
                TenantId = user.TenantId,
                IsActive = user.IsActive,
                AvatarUrl = string.IsNullOrEmpty(user.AvatarUrl) ? null : $"{user.AvatarUrl}_md.webp",
                AvatarImages = AvatarImageUrlsDto.FromBaseUrl(user.AvatarUrl)
            }
        });
    }
}
