using FoodParkDigital.Application.Tenants.Commands;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Application.Tenants.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// The business of the session: its profile and modules, logo and WhatsApp. Under /auth/my-tenant, like the guided
/// setup (<see cref="TenantSetupController"/>), so the middleware checks the membership; the requests decide who may
/// change what.
/// </summary>
[ApiController]
[Route("api/v1/auth/my-tenant")]
[Tags("Auth")]
[Authorize]
public class MyTenantController : ControllerBase
{
    private readonly IMediator _mediator;

    public MyTenantController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<TenantProfileDto>> GetMyTenant(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyTenantQuery(), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status404NotFound, result.Error);
        return Ok(result.Value);
    }

    [HttpPut]
    public async Task<ActionResult<TenantProfileDto>> UpdateMyTenant([FromBody] UpdateMyTenantCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpPost("logo")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<LogoImageUrlsDto>> UploadTenantLogo(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return this.ApiError(StatusCodes.Status400BadRequest, "No se proporcionó un archivo.");

        var result = await _mediator.Send(new UploadTenantLogoCommand
        {
            FileStream = file.OpenReadStream(),
            FileName = file.FileName,
            ContentType = file.ContentType,
            FileSize = file.Length
        }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpDelete("logo")]
    public async Task<IActionResult> DeleteTenantLogo(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteTenantLogoCommand(), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return NoContent();
    }

    [HttpGet("whatsapp")]
    public async Task<ActionResult<WhatsAppConfigDto>> GetWhatsAppConfig(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetWhatsAppConfigQuery(), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpPut("whatsapp")]
    public async Task<ActionResult<WhatsAppConfigDto>> UpdateWhatsAppConfig([FromBody] UpdateWhatsAppConfigCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpPost("whatsapp/send")]
    public async Task<IActionResult> SendWhatsAppMessage([FromBody] SendWhatsAppMessageCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(new { message = "Mensaje enviado exitosamente." });
    }
}
