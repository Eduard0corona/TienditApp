using FoodParkDigital.Application.Identity.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// Recovering the password of an account by email or by a code sent by SMS. Anonymous and rate limited as credentials
/// and codes; asking for the email or the code answers the same whether the account exists or not.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Tags("Auth")]
[AllowAnonymous]
[EnableRateLimiting("auth")]
public class PasswordRecoveryController : ControllerBase
{
    private readonly IMediator _mediator;

    public PasswordRecoveryController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("forgot-password")]
    public async Task<ActionResult> ForgotPassword([FromBody] ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        await _mediator.Send(command, cancellationToken);
        return Ok(new { message = "Si el email existe en nuestro sistema, recibirás un correo con instrucciones." });
    }

    [HttpPost("reset-password")]
    public async Task<ActionResult> ResetPassword([FromBody] ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(new { message = "Contraseña restablecida exitosamente." });
    }

    /// <summary>Recover the password with a code sent by SMS to the phone the account confirmed.</summary>
    [HttpPost("forgot-password/sms")]
    public async Task<ActionResult> ForgotPasswordBySms([FromBody] ForgotPasswordBySmsCommand command, CancellationToken cancellationToken)
    {
        await _mediator.Send(command, cancellationToken);
        return Ok(new { message = "Si tu cuenta tiene un teléfono confirmado, te enviamos un código por SMS." });
    }

    [HttpPost("reset-password/sms")]
    public async Task<ActionResult> ResetPasswordWithSmsCode([FromBody] ResetPasswordWithSmsCodeCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(new { message = "Contraseña restablecida exitosamente." });
    }
}
