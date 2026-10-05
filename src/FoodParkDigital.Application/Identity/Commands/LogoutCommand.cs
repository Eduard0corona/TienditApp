using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Identity.Services;
using FoodParkDigital.Domain.Identity.Enums;
using MediatR;

namespace FoodParkDigital.Application.Identity.Commands;

[AllowAnonymousRequest("Acceso público: inicio de sesión, registro y confirmaciones.")]
public record LogoutCommand(string? RefreshToken) : IRequest<Result>;

/// <summary>Ends the session of the refresh token: it no longer renews, and its access tokens stop working.</summary>
public class LogoutCommandHandler : IRequestHandler<LogoutCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;

    public LogoutCommandHandler(IApplicationDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<Result> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var token = await UserSessions.FindAsync(_context, request.RefreshToken, cancellationToken);
        if (token is not null)
            await UserSessions.EndSessionAsync(
                _context, token.SessionId, RefreshTokenRevocationReason.SignedOut, _clock.UtcNow, cancellationToken);

        return Result.Success();
    }
}
