using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Identity.Events;
using FluentValidation;
using MediatR;

namespace FoodParkDigital.Application.Identity.Commands;

[AllowAnonymousRequest("Acceso público: inicio de sesión, registro y confirmaciones.")]
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
    private readonly IApplicationDbContext _context;
    private readonly IOutbox _outbox;

    public ResendConfirmationEmailCommandHandler(IApplicationDbContext context, IOutbox outbox)
    {
        _context = context;
        _outbox = outbox;
    }

    public async Task<Result> Handle(ResendConfirmationEmailCommand request, CancellationToken cancellationToken)
    {
        // The same work and answer for every email: the account is found, and the link sent, after the answer
        // (AccountEmailRequestedHandler), and only while the account has not confirmed its email.
        _outbox.Add(new AccountEmailRequestedEvent(AccountEmailPurpose.EmailConfirmation, Email: request.Email.Trim()), tenantId: null);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
