using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Identity.Events;
using FluentValidation;
using MediatR;

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
    private readonly IApplicationDbContext _context;
    private readonly IOutbox _outbox;

    public ResendPhoneConfirmationCommandHandler(IApplicationDbContext context, IOutbox outbox)
    {
        _context = context;
        _outbox = outbox;
    }

    public async Task<Result> Handle(ResendPhoneConfirmationCommand request, CancellationToken cancellationToken)
    {
        // The same work and answer whether the account exists, already confirmed its phone or asked a moment ago:
        // the account is found, and the code sent, after the answer (VerificationCodeRequestedHandler).
        _outbox.Add(
            new VerificationCodeRequestedEvent(
                VerificationCodePurpose.PhoneConfirmation,
                UserId: request.UserId,
                PhoneNumber: request.UserId.HasValue ? null : request.PhoneNumber),
            tenantId: null);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
