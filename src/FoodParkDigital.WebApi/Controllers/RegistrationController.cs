using FoodParkDigital.Application.Identity.Commands;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Application.Identity.Queries;
using FoodParkDigital.WebApi.Security;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// Creating an account: registering a business or a customer, confirming the email and the phone, and accepting an
/// invitation to a team. Anonymous and rate limited as credentials and codes. Registering opens no session (the account
/// is activated from the link in the email and then signs in); accepting an invitation starts it.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Tags("Auth")]
[AllowAnonymous]
[EnableRateLimiting("auth")]
public class RegistrationController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IWebHostEnvironment _env;

    public RegistrationController(IMediator mediator, IWebHostEnvironment env)
    {
        _mediator = mediator;
        _env = env;
    }

    // Signing up opens no session: the account is activated from the link in the email and then signs in.
    private const string RegistrationEmailSentMessage = "Te enviamos un correo con un enlace para activar tu cuenta.";

    [HttpPost("register/business")]
    public async Task<ActionResult> RegisterBusiness([FromBody] RegisterBusinessCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(new { message = RegistrationEmailSentMessage });
    }

    [HttpPost("register/customer")]
    public async Task<ActionResult> RegisterCustomer([FromBody] RegisterCustomerCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(new { message = RegistrationEmailSentMessage });
    }

    [HttpGet("confirm-email")]
    public async Task<ActionResult> ConfirmEmail([FromQuery] string userId, [FromQuery] string token, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ConfirmEmailCommand(userId, token), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(new { message = "Email confirmado exitosamente." });
    }

    [HttpPost("resend-confirmation-email")]
    public async Task<ActionResult> ResendConfirmationEmail([FromBody] ResendConfirmationEmailCommand command, CancellationToken cancellationToken)
    {
        await _mediator.Send(command, cancellationToken);
        return Ok(new { message = "Si el email existe, recibirás un correo de confirmación." });
    }

    [HttpPost("confirm-phone")]
    public async Task<ActionResult> ConfirmPhone([FromBody] ConfirmPhoneCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(new { message = "Número de teléfono confirmado exitosamente." });
    }

    [HttpPost("resend-phone-confirmation")]
    public async Task<ActionResult> ResendPhoneConfirmation([FromBody] ResendPhoneConfirmationCommand command, CancellationToken cancellationToken)
    {
        await _mediator.Send(command, cancellationToken);
        return Ok(new { message = "Si el número existe, recibirás un SMS de confirmación." });
    }

    /// <summary>Fix the number typed at registration (with the password) and receive the code there.</summary>
    [HttpPost("change-unconfirmed-phone")]
    public async Task<ActionResult> ChangeUnconfirmedPhone([FromBody] ChangeUnconfirmedPhoneCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(new { message = "Te enviamos un código al número nuevo." });
    }

    /// <summary>What an invitation to a team shows before the person chooses their password.</summary>
    [HttpGet("invitation")]
    public async Task<ActionResult<InvitationDto>> GetInvitation([FromQuery] Guid userId, [FromQuery] string token, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetInvitationQuery(userId, token), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    /// <summary>The invited person chooses their password and the session starts.</summary>
    [HttpPost("accept-invitation")]
    public async Task<ActionResult<AuthResponse>> AcceptInvitation([FromBody] AcceptInvitationCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(RefreshTokenCookie.Issue(HttpContext, _env, result.Value));
    }
}
