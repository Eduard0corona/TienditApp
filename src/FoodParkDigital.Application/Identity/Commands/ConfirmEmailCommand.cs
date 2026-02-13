using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Identity.Entities;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace FoodParkDigital.Application.Identity.Commands;

public record ConfirmEmailCommand(string UserId, string Token) : IRequest<Result>;

public class ConfirmEmailCommandHandler : IRequestHandler<ConfirmEmailCommand, Result>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public ConfirmEmailCommandHandler(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<Result> Handle(ConfirmEmailCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user is null)
            return Result.Failure("Usuario no encontrado.");

        if (user.EmailConfirmed)
            return Result.Success();

        var result = await _userManager.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
            return Result.Failure("Error al confirmar el email. Token inválido o expirado.");

        return Result.Success();
    }
}
