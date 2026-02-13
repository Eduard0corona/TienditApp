using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Identity.Entities;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace FoodParkDigital.Application.Identity.Commands;

public record ResendConfirmationEmailCommand : IRequest<Result>
{
    public string Email { get; init; } = null!;
}

public class ResendConfirmationEmailCommandValidator : AbstractValidator<ResendConfirmationEmailCommand>
{
    public ResendConfirmationEmailCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}

public class ResendConfirmationEmailCommandHandler : IRequestHandler<ResendConfirmationEmailCommand, Result>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;

    public ResendConfirmationEmailCommandHandler(
        UserManager<ApplicationUser> userManager,
        IEmailService emailService)
    {
        _userManager = userManager;
        _emailService = emailService;
    }

    public async Task<Result> Handle(ResendConfirmationEmailCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);

        // Por seguridad, no revelamos si el usuario existe o no
        if (user is null || user.EmailConfirmed)
            return Result.Success();

        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        await _emailService.SendEmailConfirmationAsync(user.Email!, user.Id.ToString(), token);

        return Result.Success();
    }
}
