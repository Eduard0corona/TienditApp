using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Identity.Entities;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Commands;

public record ResendPhoneConfirmationCommand : IRequest<Result>
{
    public string PhoneNumber { get; init; } = null!;
}

public class ResendPhoneConfirmationCommandValidator : AbstractValidator<ResendPhoneConfirmationCommand>
{
    public ResendPhoneConfirmationCommandValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .Matches(@"^\+[1-9]\d{6,14}$")
            .WithMessage("El número de teléfono debe estar en formato E.164 (ej: +5215512345678).");
    }
}

public class ResendPhoneConfirmationCommandHandler : IRequestHandler<ResendPhoneConfirmationCommand, Result>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISmsService _smsService;
    private readonly IApplicationDbContext _context;

    public ResendPhoneConfirmationCommandHandler(
        UserManager<ApplicationUser> userManager,
        ISmsService smsService,
        IApplicationDbContext context)
    {
        _userManager = userManager;
        _smsService = smsService;
        _context = context;
    }

    public async Task<Result> Handle(ResendPhoneConfirmationCommand request, CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.PhoneNumber == request.PhoneNumber, cancellationToken);

        // Por seguridad, no revelamos si el usuario existe o no
        if (user is null || user.PhoneNumberConfirmed)
            return Result.Success();

        var code = await _userManager.GenerateChangePhoneNumberTokenAsync(user, user.PhoneNumber!);
        await _smsService.SendVerificationCodeAsync(user.PhoneNumber!, code);

        return Result.Success();
    }
}
