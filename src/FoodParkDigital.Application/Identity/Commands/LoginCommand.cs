using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Application.Identity.Services;
using FoodParkDigital.Domain.Identity.Entities;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Commands;

[AllowAnonymousRequest("Acceso público: inicio de sesión, registro y confirmaciones.")]
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
    private readonly IApplicationDbContext _context;
    private readonly ITwoFactorLoginChallengeService _twoFactorChallengeService;
    private readonly IAuthSessionIssuer _authSessionIssuer;

    public LoginCommandHandler(
        UserManager<ApplicationUser> userManager,
        ISignInService signInService,
        IApplicationDbContext context,
        ITwoFactorLoginChallengeService twoFactorChallengeService,
        IAuthSessionIssuer authSessionIssuer)
    {
        _userManager = userManager;
        _signInService = signInService;
        _context = context;
        _twoFactorChallengeService = twoFactorChallengeService;
        _authSessionIssuer = authSessionIssuer;
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

        // Con 2FA la contraseña solo habilita el desafío del segundo paso; los tokens los emite Login2FACommand.
        var is2FAEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
        if (is2FAEnabled)
        {
            var challengeToken = await _twoFactorChallengeService.CreateAsync(user, cancellationToken);
            if (challengeToken is null)
                return Result.Failure<AuthResponse>("No se pudo iniciar la verificación en dos pasos. Intente de nuevo.");

            return Result.Success(new AuthResponse
            {
                RequiresTwoFactor = true,
                TwoFactorChallengeToken = challengeToken
            });
        }

        return Result.Success(await _authSessionIssuer.IssueAsync(user, cancellationToken));
    }
}
