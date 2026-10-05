using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Identity.Services;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Commands;

[AllowAnonymousRequest("Acceso público: inicio de sesión, registro y confirmaciones.")]
public record ResetPasswordCommand : IRequest<Result>
{
    public string UserId { get; init; } = null!;
    public string Token { get; init; } = null!;
    public string NewPassword { get; init; } = null!;
}

public class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).NewPassword();
    }
}

public class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, Result>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;

    public ResetPasswordCommandHandler(
        UserManager<ApplicationUser> userManager,
        IApplicationDbContext context,
        IDateTimeProvider clock)
    {
        _userManager = userManager;
        _context = context;
        _clock = clock;
    }

    public async Task<Result> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id.ToString() == request.UserId, cancellationToken);
        if (user is null)
            return Result.Failure("Usuario no encontrado.");

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return Result.Failure($"Error al restablecer la contraseña: {errors}");
        }

        // Recuperar la contraseña también levanta un bloqueo por intentos fallidos.
        await AccountSecurity.UnlockAsync(_userManager, user);

        // The sessions opened with the old password end, and their access tokens stop working with the new stamp.
        await UserSessions.EndAllAsync(
            _context, user.Id, RefreshTokenRevocationReason.PasswordReset, _clock.UtcNow, cancellationToken);
        await _userManager.UpdateSecurityStampAsync(user);

        return Result.Success();
    }
}
