using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Identity.Commands;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Application.Identity.Queries;
using FoodParkDigital.Application.Tenants.Commands;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Application.Tenants.Queries;
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
// Only credential and code endpoints are rate limited ("auth": 10 per minute per IP outside Development).
// Refresh, profile and tenant endpoints run on every screen of every device of a business, often behind
// one IP: limiting them answered 429 and the client ended the session.
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IWebHostEnvironment _env;

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
    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status401Unauthorized, result.Error);

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
        var refreshToken = Request.Cookies[RefreshTokenCookie.Name];
        if (string.IsNullOrEmpty(refreshToken))
            return this.ApiError(StatusCodes.Status401Unauthorized, "No hay una sesión que renovar. Inicia sesión de nuevo.");

        var authHeader = Request.Headers.Authorization.ToString();
        var expiredToken = !string.IsNullOrEmpty(authHeader)
            ? authHeader.Replace("Bearer ", "")
            : null;

        var command = new RefreshTokenCommand(expiredToken, refreshToken);
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            DeleteRefreshTokenCookie();
            return this.ApiError(StatusCodes.Status401Unauthorized, result.Error);
        }

        if (result.Value.RefreshToken is not null)
            SetRefreshTokenCookie(result.Value.RefreshToken);

        return Ok(result.Value with { RefreshToken = null });
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<ActionResult> Logout()
    {
        var refreshToken = Request.Cookies[RefreshTokenCookie.Name];
        await _mediator.Send(new LogoutCommand(refreshToken));
        DeleteRefreshTokenCookie();
        return Ok(new { message = "Sesión cerrada exitosamente." });
    }

    [HttpPost("register/business")]
    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> RegisterBusiness([FromBody] RegisterBusinessCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        var response = result.Value;
        if (response.RefreshToken is not null)
            SetRefreshTokenCookie(response.RefreshToken);

        return Ok(response with { RefreshToken = null });
    }

    [HttpPost("register/customer")]
    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> RegisterCustomer([FromBody] RegisterCustomerCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        var response = result.Value;
        if (response.RefreshToken is not null)
            SetRefreshTokenCookie(response.RefreshToken);

        return Ok(response with { RefreshToken = null });
    }

    [HttpPost("switch-tenant")]
    [Authorize]
    public async Task<ActionResult<AuthResponse>> SwitchTenant([FromBody] SwitchTenantCommand command)
    {
        var result = await _mediator.Send(command with { CurrentRefreshToken = Request.Cookies[RefreshTokenCookie.Name] });
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        if (result.Value.RefreshToken is not null)
            SetRefreshTokenCookie(result.Value.RefreshToken);

        return Ok(result.Value with { RefreshToken = null });
    }

    [HttpGet("confirm-email")]
    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    public async Task<ActionResult> ConfirmEmail([FromQuery] string userId, [FromQuery] string token)
    {
        var result = await _mediator.Send(new ConfirmEmailCommand(userId, token));
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(new { message = "Email confirmado exitosamente." });
    }

    [HttpPost("resend-confirmation-email")]
    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    public async Task<ActionResult> ResendConfirmationEmail([FromBody] ResendConfirmationEmailCommand command)
    {
        await _mediator.Send(command);
        return Ok(new { message = "Si el email existe, recibirás un correo de confirmación." });
    }

    [HttpPost("confirm-phone")]
    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    public async Task<ActionResult> ConfirmPhone([FromBody] ConfirmPhoneCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(new { message = "Número de teléfono confirmado exitosamente." });
    }

    [HttpPost("resend-phone-confirmation")]
    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    public async Task<ActionResult> ResendPhoneConfirmation([FromBody] ResendPhoneConfirmationCommand command)
    {
        await _mediator.Send(command);
        return Ok(new { message = "Si el número existe, recibirás un SMS de confirmación." });
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    public async Task<ActionResult> ForgotPassword([FromBody] ForgotPasswordCommand command)
    {
        await _mediator.Send(command);
        return Ok(new { message = "Si el email existe en nuestro sistema, recibirás un correo con instrucciones." });
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    public async Task<ActionResult> ResetPassword([FromBody] ResetPasswordCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(new { message = "Contraseña restablecida exitosamente." });
    }

    /// <summary>Recover the password with a code sent by SMS to the phone the account confirmed.</summary>
    [HttpPost("forgot-password/sms")]
    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    public async Task<ActionResult> ForgotPasswordBySms([FromBody] ForgotPasswordBySmsCommand command)
    {
        await _mediator.Send(command);
        return Ok(new { message = "Si tu cuenta tiene un teléfono confirmado, te enviamos un código por SMS." });
    }

    [HttpPost("reset-password/sms")]
    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    public async Task<ActionResult> ResetPasswordWithSmsCode([FromBody] ResetPasswordWithSmsCodeCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(new { message = "Contraseña restablecida exitosamente." });
    }

    /// <summary>Fix the number typed at registration (with the password) and receive the code there.</summary>
    [HttpPost("change-unconfirmed-phone")]
    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    public async Task<ActionResult> ChangeUnconfirmedPhone([FromBody] ChangeUnconfirmedPhoneCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(new { message = "Te enviamos un código al número nuevo." });
    }

    /// <summary>What an invitation to a team shows before the person chooses their password.</summary>
    [HttpGet("invitation")]
    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    public async Task<ActionResult<InvitationDto>> GetInvitation([FromQuery] Guid userId, [FromQuery] string token)
    {
        var result = await _mediator.Send(new GetInvitationQuery(userId, token));
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    /// <summary>The invited person chooses their password and the session starts.</summary>
    [HttpPost("accept-invitation")]
    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> AcceptInvitation([FromBody] AcceptInvitationCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        var response = result.Value;
        if (response.RefreshToken is not null)
            SetRefreshTokenCookie(response.RefreshToken);

        return Ok(response with { RefreshToken = null });
    }

    [HttpGet("profile")]
    [Authorize]
    public async Task<ActionResult<UserProfileDto>> GetProfile()
    {
        var result = await _mediator.Send(new GetUserProfileQuery());
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status404NotFound, result.Error);
        return Ok(result.Value);
    }

    [HttpPut("profile")]
    [Authorize]
    public async Task<ActionResult<UserProfileDto>> UpdateProfile([FromBody] UpdateUserProfileCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpPost("profile/avatar")]
    [Authorize]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<AvatarImageUrlsDto>> UploadAvatar(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return this.ApiError(StatusCodes.Status400BadRequest, "No se proporcionó un archivo.");

        var result = await _mediator.Send(new UploadAvatarCommand
        {
            FileStream = file.OpenReadStream(),
            FileName = file.FileName,
            ContentType = file.ContentType,
            FileSize = file.Length
        });
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpDelete("profile/avatar")]
    [Authorize]
    public async Task<IActionResult> DeleteAvatar()
    {
        var result = await _mediator.Send(new DeleteAvatarCommand());
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return NoContent();
    }

    /// <summary>Change the phone of the account: with the password, a code goes to the new number.</summary>
    [HttpPost("profile/phone")]
    [EnableRateLimiting("auth")]
    [Authorize]
    public async Task<IActionResult> RequestPhoneChange([FromBody] RequestPhoneChangeCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(new { message = "Te enviamos un código al número nuevo." });
    }

    [HttpPost("profile/phone/confirm")]
    [EnableRateLimiting("auth")]
    [Authorize]
    public async Task<IActionResult> ConfirmPhoneChange([FromBody] ConfirmPhoneChangeCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(new { message = "Número de teléfono confirmado exitosamente." });
    }

    [HttpPost("profile/change-password")]
    [EnableRateLimiting("auth")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(new { message = "Contraseña actualizada exitosamente." });
    }

    [HttpGet("profile/whatsapp")]
    public async Task<ActionResult<OwnerWhatsAppConfigDto>> GetOwnerWhatsAppConfig()
    {
        var result = await _mediator.Send(new GetOwnerWhatsAppConfigQuery());
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpPut("profile/whatsapp")]
    public async Task<ActionResult<OwnerWhatsAppConfigDto>> UpdateOwnerWhatsAppConfig([FromBody] UpdateOwnerWhatsAppConfigCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpGet("my-tenant")]
    [Authorize]
    public async Task<ActionResult<TenantProfileDto>> GetMyTenant()
    {
        var result = await _mediator.Send(new GetMyTenantQuery());
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status404NotFound, result.Error);
        return Ok(result.Value);
    }

    [HttpPut("my-tenant")]
    [Authorize]
    public async Task<ActionResult<TenantProfileDto>> UpdateMyTenant([FromBody] UpdateMyTenantCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpPost("my-tenant/logo")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<LogoImageUrlsDto>> UploadTenantLogo(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return this.ApiError(StatusCodes.Status400BadRequest, "No se proporcionó un archivo.");

        var result = await _mediator.Send(new UploadTenantLogoCommand
        {
            FileStream = file.OpenReadStream(),
            FileName = file.FileName,
            ContentType = file.ContentType,
            FileSize = file.Length
        });
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpDelete("my-tenant/logo")]
    public async Task<IActionResult> DeleteTenantLogo()
    {
        var result = await _mediator.Send(new DeleteTenantLogoCommand());
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return NoContent();
    }

    [HttpGet("my-tenant/whatsapp")]
    public async Task<ActionResult<WhatsAppConfigDto>> GetWhatsAppConfig()
    {
        var result = await _mediator.Send(new GetWhatsAppConfigQuery());
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpPut("my-tenant/whatsapp")]
    public async Task<ActionResult<WhatsAppConfigDto>> UpdateWhatsAppConfig([FromBody] UpdateWhatsAppConfigCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpPost("my-tenant/whatsapp/send")]
    public async Task<IActionResult> SendWhatsAppMessage([FromBody] SendWhatsAppMessageCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(new { message = "Mensaje enviado exitosamente." });
    }

    private void SetRefreshTokenCookie(string refreshToken)
        => RefreshTokenCookie.Append(HttpContext, _env, refreshToken);

    private void DeleteRefreshTokenCookie()
        => RefreshTokenCookie.Delete(HttpContext, _env);
}
