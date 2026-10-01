using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Identity.Commands;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Application.Identity.Queries;
using FoodParkDigital.Application.Tenants.Commands;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Application.Tenants.Queries;
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

    [HttpPost("register/business")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> RegisterBusiness([FromBody] RegisterBusinessCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });

        var response = result.Value;
        if (response.RefreshToken is not null)
            SetRefreshTokenCookie(response.RefreshToken);

        return Ok(response with { RefreshToken = null });
    }

    [HttpPost("register/customer")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> RegisterCustomer([FromBody] RegisterCustomerCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });

        var response = result.Value;
        if (response.RefreshToken is not null)
            SetRefreshTokenCookie(response.RefreshToken);

        return Ok(response with { RefreshToken = null });
    }

    [HttpPost("switch-tenant")]
    [Authorize]
    public async Task<ActionResult<AuthResponse>> SwitchTenant([FromBody] SwitchTenantCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });

        if (result.Value.RefreshToken is not null)
            SetRefreshTokenCookie(result.Value.RefreshToken);

        return Ok(result.Value with { RefreshToken = null });
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

    [HttpPost("confirm-phone")]
    [AllowAnonymous]
    public async Task<ActionResult> ConfirmPhone([FromBody] ConfirmPhoneCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(new { message = "Número de teléfono confirmado exitosamente." });
    }

    [HttpPost("resend-phone-confirmation")]
    [AllowAnonymous]
    public async Task<ActionResult> ResendPhoneConfirmation([FromBody] ResendPhoneConfirmationCommand command)
    {
        await _mediator.Send(command);
        return Ok(new { message = "Si el número existe, recibirás un SMS de confirmación." });
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
    public async Task<ActionResult<UserProfileDto>> GetProfile()
    {
        var result = await _mediator.Send(new GetUserProfileQuery());
        if (result.IsFailure)
            return NotFound(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpPut("profile")]
    [Authorize]
    public async Task<ActionResult<UserProfileDto>> UpdateProfile([FromBody] UpdateUserProfileCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpPost("profile/avatar")]
    [Authorize]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<AvatarImageUrlsDto>> UploadAvatar(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No se proporcionó un archivo." });

        var result = await _mediator.Send(new UploadAvatarCommand
        {
            FileStream = file.OpenReadStream(),
            FileName = file.FileName,
            ContentType = file.ContentType,
            FileSize = file.Length
        });
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpDelete("profile/avatar")]
    [Authorize]
    public async Task<IActionResult> DeleteAvatar()
    {
        var result = await _mediator.Send(new DeleteAvatarCommand());
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return NoContent();
    }

    [HttpPost("profile/change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(new { message = "Contraseña actualizada exitosamente." });
    }

    [HttpGet("profile/whatsapp")]
    [Authorize(Roles = "TENANT_ADMIN,SUPER_ADMIN")]
    public async Task<ActionResult<OwnerWhatsAppConfigDto>> GetOwnerWhatsAppConfig()
    {
        var result = await _mediator.Send(new GetOwnerWhatsAppConfigQuery());
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpPut("profile/whatsapp")]
    [Authorize(Roles = "TENANT_ADMIN,SUPER_ADMIN")]
    public async Task<ActionResult<OwnerWhatsAppConfigDto>> UpdateOwnerWhatsAppConfig([FromBody] UpdateOwnerWhatsAppConfigCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpGet("my-tenant")]
    [Authorize]
    public async Task<ActionResult<TenantProfileDto>> GetMyTenant()
    {
        var result = await _mediator.Send(new GetMyTenantQuery());
        if (result.IsFailure)
            return NotFound(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpPut("my-tenant")]
    [Authorize]
    public async Task<ActionResult<TenantProfileDto>> UpdateMyTenant([FromBody] UpdateMyTenantCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpPost("my-tenant/logo")]
    [Authorize(Roles = "TENANT_ADMIN,SUPER_ADMIN")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<LogoImageUrlsDto>> UploadTenantLogo(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No se proporcionó un archivo." });

        var result = await _mediator.Send(new UploadTenantLogoCommand
        {
            FileStream = file.OpenReadStream(),
            FileName = file.FileName,
            ContentType = file.ContentType,
            FileSize = file.Length
        });
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpDelete("my-tenant/logo")]
    [Authorize(Roles = "TENANT_ADMIN,SUPER_ADMIN")]
    public async Task<IActionResult> DeleteTenantLogo()
    {
        var result = await _mediator.Send(new DeleteTenantLogoCommand());
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return NoContent();
    }

    [HttpGet("my-tenant/whatsapp")]
    [Authorize(Roles = "TENANT_ADMIN,SUPER_ADMIN")]
    public async Task<ActionResult<WhatsAppConfigDto>> GetWhatsAppConfig()
    {
        var result = await _mediator.Send(new GetWhatsAppConfigQuery());
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpPut("my-tenant/whatsapp")]
    [Authorize(Roles = "TENANT_ADMIN,SUPER_ADMIN")]
    public async Task<ActionResult<WhatsAppConfigDto>> UpdateWhatsAppConfig([FromBody] UpdateWhatsAppConfigCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpPost("my-tenant/whatsapp/send")]
    [Authorize(Roles = "TENANT_ADMIN,STAFF,SUPER_ADMIN")]
    public async Task<IActionResult> SendWhatsAppMessage([FromBody] SendWhatsAppMessageCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(new { message = "Mensaje enviado exitosamente." });
    }

    private CookieOptions GetCookieOptions(DateTime? expires = null)
    {
        // Outside Development the cookie is always Secure, even when TLS terminates at a proxy and
        // the request reaches the app as HTTP.
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
