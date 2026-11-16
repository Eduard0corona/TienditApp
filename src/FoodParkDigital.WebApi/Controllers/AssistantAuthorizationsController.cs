using FoodParkDigital.Application.Assistants;
using FoodParkDigital.WebApi.Errors;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// The permission page of an AI assistant (Fase 32E, ADR 0097): what it shows of a request, and the customer's answer, with
/// their session. The request is the one the assistant sent to /oauth/authorize, as the page received it.
/// </summary>
[ApiController]
[Route("api/v1/auth/assistant-authorizations")]
[Authorize]
public class AssistantAuthorizationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AssistantAuthorizationsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Whether the request may ask the customer, with the assistant's name and the site it returns to; nothing is given.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<AssistantAuthorizationCheckDto>> Check(
        [FromQuery] string? responseType,
        [FromQuery] string? clientId,
        [FromQuery] string? redirectUri,
        [FromQuery] string? codeChallenge,
        [FromQuery] string? codeChallengeMethod,
        [FromQuery] string? resource,
        [FromQuery] string? scope,
        [FromQuery] string? state,
        CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new CheckAssistantAuthorizationQuery
        {
            ResponseType = responseType,
            ClientId = clientId,
            RedirectUri = redirectUri,
            CodeChallenge = codeChallenge,
            CodeChallengeMethod = codeChallengeMethod,
            Resource = resource,
            Scope = scope,
            State = state
        }, cancellationToken));

    /// <summary>
    /// The customer allows the assistant, or does not: where the browser goes, the assistant with its code or with
    /// access_denied. Only a customer account.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<AssistantAuthorizationRedirectDto>> Answer(
        [FromBody] ApproveAssistantAuthorizationCommand request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(request, cancellationToken);
        if (result.IsSuccess)
            return Ok(result.Value);
        return this.ApiError(
            result.Error == AssistantAuthorization.CustomersOnlyMessage ? StatusCodes.Status403Forbidden : StatusCodes.Status400BadRequest,
            result.Error!);
    }
}
