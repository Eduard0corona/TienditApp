using FoodParkDigital.Application.Identity.Commands.TwoFactor;
using FoodParkDigital.Application.Identity.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
[EnableRateLimiting("auth")]
public class TwoFactorController : ControllerBase
{
    private readonly IMediator _mediator;

    public TwoFactorController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("enable")]
    public async Task<ActionResult<Enable2FAResponse>> Enable2FA()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized(new { error = "Usuario no autenticado." });

        var result = await _mediator.Send(new Enable2FACommand(userId));
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });

        return Ok(result.Value);
    }

    [HttpPost("verify")]
    public async Task<ActionResult<Verify2FAResponse>> Verify2FA([FromBody] VerifyRequest request)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized(new { error = "Usuario no autenticado." });

        var result = await _mediator.Send(new Verify2FACommand(userId, request.Code));
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });

        return Ok(result.Value);
    }

    [HttpPost("disable")]
    public async Task<ActionResult> Disable2FA()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized(new { error = "Usuario no autenticado." });

        var result = await _mediator.Send(new Disable2FACommand(userId));
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });

        return Ok(new { message = "Autenticación de dos factores deshabilitada exitosamente." });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login2FA([FromBody] Login2FACommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return Unauthorized(new { error = result.Error });

        return Ok(result.Value);
    }
}

public record VerifyRequest(string Code);
