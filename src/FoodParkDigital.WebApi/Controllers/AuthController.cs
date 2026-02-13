using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Identity.Commands;
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
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IWebHostEnvironment _env;
    private const string RefreshTokenCookieName = "refresh_token";

    public AuthController(
        IMediator mediator,
        IJwtTokenService jwtTokenService,
        IWebHostEnvironment env)
    {
        _mediator = mediator;
        _jwtTokenService = jwtTokenService;
        _env = env;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return Unauthorized(new { error = result.Error });

        var response = result.Value;

        if (response.RequiresTwoFactor)
            return Ok(response);

        if (response.RefreshToken is not null)
            SetRefreshTokenCookie(response.RefreshToken);

        return Ok(response with { RefreshToken = null });
    }

    [HttpPost("refresh-token")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> RefreshToken()
    {
        var refreshToken = Request.Cookies[RefreshTokenCookieName];
        if (string.IsNullOrEmpty(refreshToken))
            return Unauthorized(new { error = "No refresh token provided." });

        var authHeader = Request.Headers.Authorization.ToString();
        var expiredToken = !string.IsNullOrEmpty(authHeader)
            ? authHeader.Replace("Bearer ", "")
            : null;

        var command = new RefreshTokenCommand(expiredToken, refreshToken);
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            DeleteRefreshTokenCookie();
            return Unauthorized(new { error = result.Error });
        }

        if (result.Value.RefreshToken is not null)
            SetRefreshTokenCookie(result.Value.RefreshToken);

        return Ok(result.Value with { RefreshToken = null });
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<ActionResult> Logout()
    {
        var refreshToken = Request.Cookies[RefreshTokenCookieName];
        await _mediator.Send(new LogoutCommand(refreshToken));
        DeleteRefreshTokenCookie();
        return Ok(new { message = "Sesión cerrada exitosamente." });
    }

    [HttpPost("register")]
    [Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN")]
    public async Task<ActionResult<UserDto>> Register([FromBody] RegisterUserCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return CreatedAtAction(nameof(Register), new { id = result.Value.Id }, result.Value);
    }

    [HttpGet("confirm-email")]
    [AllowAnonymous]
    public async Task<ActionResult> ConfirmEmail([FromQuery] string userId, [FromQuery] string token)
    {
        var result = await _mediator.Send(new ConfirmEmailCommand(userId, token));
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(new { message = "Email confirmado exitosamente." });
    }

    [HttpPost("resend-confirmation-email")]
    [AllowAnonymous]
    public async Task<ActionResult> ResendConfirmationEmail([FromBody] ResendConfirmationEmailCommand command)
    {
        await _mediator.Send(command);
        return Ok(new { message = "Si el email existe, recibirás un correo de confirmación." });
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<ActionResult> ForgotPassword([FromBody] ForgotPasswordCommand command)
    {
        await _mediator.Send(command);
        return Ok(new { message = "Si el email existe en nuestro sistema, recibirás un correo con instrucciones." });
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<ActionResult> ResetPassword([FromBody] ResetPasswordCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(new { message = "Contraseña restablecida exitosamente." });
    }

    [HttpGet("profile")]
    [Authorize]
    public ActionResult GetProfile()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        var name = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        var tenantId = User.FindFirst("tenant_id")?.Value;

        return Ok(new
        {
            UserId = userId,
            Email = email,
            Name = name,
            Role = role,
            TenantId = tenantId
        });
    }

    private CookieOptions GetCookieOptions(DateTime? expires = null)
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = !_env.IsDevelopment(),
            SameSite = _env.IsDevelopment() ? SameSiteMode.Lax : SameSiteMode.Strict,
            Expires = expires,
            Path = "/api/v1/auth"
        };
    }

    private void SetRefreshTokenCookie(string refreshToken)
    {
        var cookieOptions = GetCookieOptions(DateTime.UtcNow.AddDays(7));
        Response.Cookies.Append(RefreshTokenCookieName, refreshToken, cookieOptions);
    }

    private void DeleteRefreshTokenCookie()
    {
        Response.Cookies.Delete(RefreshTokenCookieName, GetCookieOptions());
    }
}
