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
    /// <summary>
    /// Every refused attempt says this, an account not activated yet included: it also tells whoever got locked, or
    /// has not opened their activation link, why. Since the Fase 23A a sign-up with an email that already has an
    /// account answers as if it created one, so the right password of a new, unactivated account must not answer
    /// differently from a wrong one: signing up with someone's email and then signing in would tell whether it had one.
    /// </summary>
    public const string InvalidCredentialsMessage =
        "Correo o contraseña incorrectos, o tu cuenta aún no está activa (actívala con el enlace que te enviamos por correo). Después de varios intentos la cuenta se bloquea unos minutos; puedes recuperar tu contraseña.";

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

        // An email without an account, an inactive or locked account and a wrong password get the same answer, in about
        // the same time. A locked account answers so even to the right password: otherwise guessing it while locked
        // would still tell which guess is right.
        if (user is null || !user.IsActive || await _userManager.IsLockedOutAsync(user))
        {
            LoginTiming.CheckAgainstNoAccount(_userManager, request.Password);
            return Result.Failure<AuthResponse>(InvalidCredentialsMessage);
        }

        // Wrong passwords count per account (besides the per-IP limit): guessing from many addresses locks the account
        // for a while. Recovering the password lifts it. The attempt that locks it still answers as a wrong password.
        var signInResult = await _signInService.CheckPasswordSignInAsync(user.Id, request.Password, lockoutOnFailure: true);
        if (!signInResult.Succeeded)
            return Result.Failure<AuthResponse>(InvalidCredentialsMessage);

        // An account not activated yet answers like a wrong password, even to the right one (see the message).
        if (!user.EmailConfirmed && !user.PhoneNumberConfirmed)
            return Result.Failure<AuthResponse>(InvalidCredentialsMessage);

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
