using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Identity.Entities;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Commands;

/// <summary>
/// Sends a new phone confirmation code. The confirmation screen identifies the account by the user id
/// it received at registration (the phone is stored as typed, so it may not match an E.164 search).
/// </summary>
[AllowAnonymousRequest("Acceso público: inicio de sesión, registro y confirmaciones.")]
public record ResendPhoneConfirmationCommand : IRequest<Result>
{
    public Guid? UserId { get; init; }
    public string? PhoneNumber { get; init; }
}

public class ResendPhoneConfirmationCommandValidator : AbstractValidator<ResendPhoneConfirmationCommand>
{
    public ResendPhoneConfirmationCommandValidator()
    {
        RuleFor(x => x)
            .Must(x => x.UserId.HasValue || !string.IsNullOrWhiteSpace(x.PhoneNumber))
            .WithName("UserId")
            .WithMessage("Indica la cuenta o el teléfono al que se envía el código.");

        RuleFor(x => x.PhoneNumber)
            .Matches(@"^\+[1-9]\d{6,14}$")
            .When(x => !x.UserId.HasValue && !string.IsNullOrWhiteSpace(x.PhoneNumber))
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
        var users = _context.Users.IgnoreQueryFilters();
        var user = request.UserId.HasValue
            ? await users.FirstOrDefaultAsync(u => u.Id == request.UserId.Value, cancellationToken)
            : await users.FirstOrDefaultAsync(u => u.PhoneNumber == request.PhoneNumber, cancellationToken);

        // Por seguridad, no revelamos si el usuario existe o no
        if (user is null || user.PhoneNumberConfirmed || string.IsNullOrWhiteSpace(user.PhoneNumber))
            return Result.Success();

        var code = await _userManager.GenerateChangePhoneNumberTokenAsync(user, user.PhoneNumber!);
        await _smsService.SendVerificationCodeAsync(user.PhoneNumber!, code);

        return Result.Success();
    }
}
