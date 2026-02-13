using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using MediatR;

namespace FoodParkDigital.Application.Identity.Commands;

public record LogoutCommand(string? RefreshToken) : IRequest<Result>;

public class LogoutCommandHandler : IRequestHandler<LogoutCommand, Result>
{
    private readonly IJwtTokenService _jwtTokenService;

    public LogoutCommandHandler(IJwtTokenService jwtTokenService)
    {
        _jwtTokenService = jwtTokenService;
    }

    public async Task<Result> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(request.RefreshToken))
        {
            await _jwtTokenService.RevokeRefreshTokenAsync(request.RefreshToken);
        }

        return Result.Success();
    }
}
