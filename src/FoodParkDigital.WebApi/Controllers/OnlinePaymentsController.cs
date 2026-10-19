using FoodParkDigital.Application.OnlinePayments;
using FoodParkDigital.WebApi.Errors;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// Configuración › Pagos en línea (Fase 24I, ADR 0031), for owners and admins: connecting the business's own Mercado Pago
/// account, where its customers' online payments go, and turning paying online on in the menu.
/// </summary>
[ApiController]
[Route("api/v1/online-payments")]
[Authorize]
public class OnlinePaymentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public OnlinePaymentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<OnlinePaymentSettingsDto>> GetSettings(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetOnlinePaymentSettingsQuery(), cancellationToken));

    /// <summary>
    /// Where to send the owner to authorize TienditApp in Mercado Pago; it comes back to Configuración › Pagos en línea with
    /// the code and the state, to finish with <see cref="CompleteConnection"/>.
    /// </summary>
    [HttpPost("connect")]
    public async Task<ActionResult<OnlinePaymentConnectionStartDto>> StartConnection(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new StartOnlinePaymentConnectionCommand(), cancellationToken);
        return result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : Ok(result.Value);
    }

    /// <summary>Finishes connecting the account with what Mercado Pago sent back: once, within ten minutes, by whoever started it.</summary>
    [HttpPost("connect/complete")]
    public async Task<ActionResult<OnlinePaymentSettingsDto>> CompleteConnection(
        [FromBody] CompleteOnlinePaymentConnectionRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CompleteOnlinePaymentConnectionCommand(request.State, request.Code), cancellationToken);
        return result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : Ok(result.Value);
    }

    /// <summary>Disconnects the account and deletes its keys; not while payments or refunds are in course.</summary>
    [HttpDelete("account")]
    public async Task<ActionResult<OnlinePaymentSettingsDto>> Disconnect(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DisconnectOnlinePaymentAccountCommand(), cancellationToken);
        return result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : Ok(result.Value);
    }

    /// <summary>Turns paying online on or off in the online menu.</summary>
    [HttpPut("accepts-payments")]
    public async Task<ActionResult<OnlinePaymentSettingsDto>> SetAcceptsPayments(
        [FromBody] SetOnlinePaymentsAcceptedRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SetOnlinePaymentsAcceptedCommand(request.Accepts), cancellationToken);
        return result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : Ok(result.Value);
    }
}

public class CompleteOnlinePaymentConnectionRequest
{
    /// <summary>The <c>state</c> Mercado Pago sent back.</summary>
    public Guid State { get; init; }
    /// <summary>The <c>code</c> Mercado Pago sent back.</summary>
    public string Code { get; init; } = string.Empty;
}

public class SetOnlinePaymentsAcceptedRequest
{
    public bool Accepts { get; init; }
}
