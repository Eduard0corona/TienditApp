using FoodParkDigital.Application.Identity.Commands.TwoFactor;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.WebApi.Security;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class TwoFactorController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IWebHostEnvironment _env;

    public TwoFactorController(IMediator mediator, IWebHostEnvironment env)
    {
        _mediator = mediator;
        _env = env;
    }

    [HttpPost("enable")]
    public async Task<ActionResult<Enable2FAResponse>> Enable2FA(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return this.ApiError(StatusCodes.Status401Unauthorized, "Usuario no autenticado.");

        var result = await _mediator.Send(new Enable2FACommand(userId), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(result.Value);
    }

    [HttpPost("verify")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<Verify2FAResponse>> Verify2FA([FromBody] VerifyRequest request, CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return this.ApiError(StatusCodes.Status401Unauthorized, "Usuario no autenticado.");

        var result = await _mediator.Send(new Verify2FACommand(userId, request.Code), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(result.Value);
    }

    [HttpPost("disable")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult> Disable2FA(
        [FromBody] Disable2FARequest request,
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return this.ApiError(StatusCodes.Status401Unauthorized, "Usuario no autenticado.");

        var result = await _mediator.Send(
            new Disable2FACommand(userId, request.Code, request.UseRecoveryCode),
            cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(new { message = "Autenticación de dos factores deshabilitada exitosamente." });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponse>> Login2FA(
        [FromBody] Login2FACommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status401Unauthorized, result.Error);

        // Igual que /auth/login: el refresh token solo viaja en la cookie HttpOnly.
        if (result.Value.RefreshToken is not null)
            RefreshTokenCookie.Append(HttpContext, _env, result.Value.RefreshToken);

        return Ok(result.Value with { RefreshToken = null });
    }
}

public record VerifyRequest(string Code);

public record Disable2FARequest(string Code, bool UseRecoveryCode = false);
