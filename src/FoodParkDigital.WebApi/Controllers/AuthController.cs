using FoodParkDigital.Application.Identity.Commands;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Application.Tenants.Commands;
using FoodParkDigital.WebApi.Security;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// The session: signing in, renewing it with the refresh cookie, signing out and changing business. The rest of the
/// account lives under /auth too, in its own controllers: registration and confirmations
/// (<see cref="RegistrationController"/>), password recovery (<see cref="PasswordRecoveryController"/>), the profile
/// (<see cref="ProfileController"/>) and the business of the session (<see cref="MyTenantController"/>).
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
// Only credential and code endpoints are rate limited ("auth": 10 per minute per IP outside Development).
// Refresh, profile and tenant endpoints run on every screen of every device of a business, often behind
// one IP: limiting them answered 429 and the client ended the session.
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IWebHostEnvironment _env;

    public AuthController(IMediator mediator, IWebHostEnvironment env)
    {
        _mediator = mediator;
        _env = env;
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status401Unauthorized, result.Error);

        var response = result.Value;

        if (response.RequiresTwoFactor)
            return Ok(response);

        return Ok(RefreshTokenCookie.Issue(HttpContext, _env, response));
    }

    [HttpPost("refresh-token")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> RefreshToken(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies[RefreshTokenCookie.Name];
        if (string.IsNullOrEmpty(refreshToken))
            return this.ApiError(StatusCodes.Status401Unauthorized, "No hay una sesión que renovar. Inicia sesión de nuevo.");

        var result = await _mediator.Send(new RefreshTokenCommand(refreshToken), cancellationToken);

        if (result.IsFailure)
        {
            // Another request renewed the session with this token a moment earlier: its answer set the new cookie,
            // which stays. The client tries again.
            if (result.Error == RefreshTokenCommandHandler.ConcurrentRenewalError)
                return this.ApiError(StatusCodes.Status409Conflict, result.Error, ApiErrorCodes.ConcurrencyConflict);

            RefreshTokenCookie.Delete(HttpContext, _env);
            return this.ApiError(StatusCodes.Status401Unauthorized, result.Error);
        }

        return Ok(RefreshTokenCookie.Issue(HttpContext, _env, result.Value));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<ActionResult> Logout(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies[RefreshTokenCookie.Name];
        await _mediator.Send(new LogoutCommand(refreshToken), cancellationToken);
        RefreshTokenCookie.Delete(HttpContext, _env);
        return Ok(new { message = "Sesión cerrada exitosamente." });
    }

    [HttpPost("switch-tenant")]
    public async Task<ActionResult<AuthResponse>> SwitchTenant([FromBody] SwitchTenantCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command with { CurrentRefreshToken = Request.Cookies[RefreshTokenCookie.Name] }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(RefreshTokenCookie.Issue(HttpContext, _env, result.Value));
    }
}
