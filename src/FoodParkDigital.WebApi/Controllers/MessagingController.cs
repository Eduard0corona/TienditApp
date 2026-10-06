using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Messaging.Campaigns;
using FoodParkDigital.Application.Messaging.DTOs;
using FoodParkDigital.Application.Messaging.OptOuts;
using FoodParkDigital.Application.Messaging.Settings;
using FoodParkDigital.Application.Messaging.Templates;
using FoodParkDigital.Domain.Messaging.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// Templates, campaigns, opt-outs and appointment reminders. Staff need ManageCampaigns (opt-outs also accept
/// ManageCustomers), as each request declares.
/// </summary>
[ApiController]
[Route("api/v1/messaging")]
[Authorize]
public class MessagingController : ControllerBase
{
    private readonly IMediator _mediator;

    public MessagingController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("templates")]
    public async Task<ActionResult<List<MessageTemplateDto>>> GetTemplates(
        [FromQuery] MessagePurpose? purpose,
        [FromQuery] bool includeInactive = true,
        CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(new GetMessageTemplatesQuery { Purpose = purpose, IncludeInactive = includeInactive }, cancellationToken));

    [HttpPost("templates")]
    public async Task<ActionResult<MessageTemplateDto>> CreateTemplate([FromBody] CreateMessageTemplateCommand command, CancellationToken cancellationToken)
        => ToActionResult(await _mediator.Send(command, cancellationToken));

    [HttpPut("templates/{id:guid}")]
    public async Task<ActionResult<MessageTemplateDto>> UpdateTemplate(Guid id, [FromBody] UpdateMessageTemplateCommand command, CancellationToken cancellationToken)
        => ToActionResult(await _mediator.Send(command with { Id = id }, cancellationToken));

    [HttpGet("campaigns")]
    public async Task<ActionResult<PaginatedList<CampaignDto>>> GetCampaigns(
        [FromQuery] CampaignStatus? status,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(new GetCampaignsQuery { Status = status, PageNumber = pageNumber, PageSize = pageSize }, cancellationToken));

    [HttpGet("campaigns/{id:guid}")]
    public async Task<ActionResult<CampaignDto>> GetCampaign(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await _mediator.Send(new GetCampaignQuery(id), cancellationToken);
        return campaign is null ? NotFound() : Ok(campaign);
    }

    /// <summary>How many customers a campaign would reach now, and what the first ones would read.</summary>
    [HttpPost("campaigns/preview")]
    public async Task<ActionResult<CampaignPreviewDto>> PreviewCampaign([FromBody] PreviewCampaignQuery query, CancellationToken cancellationToken)
        => ToActionResult(await _mediator.Send(query, cancellationToken));

    [HttpPost("campaigns")]
    public async Task<ActionResult<CampaignDto>> CreateCampaign([FromBody] CreateCampaignCommand command, CancellationToken cancellationToken)
        => ToActionResult(await _mediator.Send(command, cancellationToken));

    [HttpPost("campaigns/{id:guid}/cancel")]
    public async Task<ActionResult<CampaignDto>> CancelCampaign(Guid id, CancellationToken cancellationToken)
        => ToActionResult(await _mediator.Send(new CancelCampaignCommand(id), cancellationToken));

    /// <summary>Messages of a campaign or the appointment reminders, newest first.</summary>
    [HttpGet("messages")]
    public async Task<ActionResult<PaginatedList<OutgoingMessageDto>>> GetMessages(
        [FromQuery] Guid? campaignId,
        [FromQuery] MessagePurpose? purpose,
        [FromQuery] OutgoingMessageStatus? status,
        [FromQuery] string? search,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(new GetOutgoingMessagesQuery
        {
            CampaignId = campaignId,
            Purpose = purpose,
            Status = status,
            Search = search,
            PageNumber = pageNumber,
            PageSize = pageSize
        }, cancellationToken));

    [HttpGet("opt-outs")]
    public async Task<ActionResult<PaginatedList<MessagingOptOutDto>>> GetOptOuts(
        [FromQuery] string? search,
        [FromQuery] bool includeResubscribed = false,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(new GetMessagingOptOutsQuery
        {
            Search = search,
            IncludeResubscribed = includeResubscribed,
            PageNumber = pageNumber,
            PageSize = pageSize
        }, cancellationToken));

    [HttpPost("opt-outs")]
    public async Task<IActionResult> SetOptOut([FromBody] SetMessagingOptOutCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : NoContent();
    }

    [HttpGet("settings")]
    public async Task<ActionResult<MessagingSettingsDto>> GetSettings(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetMessagingSettingsQuery(), cancellationToken));

    [HttpPut("settings/reminders")]
    public async Task<ActionResult<MessagingSettingsDto>> UpdateReminders([FromBody] UpdateAppointmentRemindersCommand command, CancellationToken cancellationToken)
        => ToActionResult(await _mediator.Send(command, cancellationToken));

    private ActionResult<T> ToActionResult<T>(Result<T> result)
        => result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : Ok(result.Value);
}
