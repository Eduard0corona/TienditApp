using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Identity.Entities;
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
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(6);
    }
}

public class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, Result>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IApplicationDbContext _context;

    public ResetPasswordCommandHandler(
        UserManager<ApplicationUser> userManager,
        IJwtTokenService jwtTokenService,
        IApplicationDbContext context)
    {
        _userManager = userManager;
        _jwtTokenService = jwtTokenService;
        _context = context;
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

        // Revocar todos los refresh tokens por seguridad
        await _jwtTokenService.RevokeAllUserRefreshTokensAsync(user.Id);

        // Actualizar security stamp (invalida tokens JWT existentes)
        await _userManager.UpdateSecurityStampAsync(user);

        return Result.Success();
    }
}
