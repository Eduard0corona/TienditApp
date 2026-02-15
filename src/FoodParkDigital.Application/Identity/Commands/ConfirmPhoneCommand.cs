using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Identity.Entities;
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
        RuleFor(x => x.Code).NotEmpty().Length(6);
    }
}

public class ConfirmPhoneCommandHandler : IRequestHandler<ConfirmPhoneCommand, Result>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApplicationDbContext _context;

    public ConfirmPhoneCommandHandler(
        UserManager<ApplicationUser> userManager,
        IApplicationDbContext context)
    {
        _userManager = userManager;
        _context = context;
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
            return Result.Failure("El usuario no tiene un número de teléfono registrado.");

        var result = await _userManager.ChangePhoneNumberAsync(user, user.PhoneNumber, request.Code);
        if (!result.Succeeded)
            return Result.Failure("Código inválido o expirado.");

        return Result.Success();
    }
}
