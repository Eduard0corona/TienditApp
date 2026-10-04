using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.WhatsApp.Commands;
using FoodParkDigital.Application.WhatsApp.DTOs;
using FoodParkDigital.Application.WhatsApp.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/whatsapp/chat")]
[Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
public class WhatsAppChatController : ControllerBase
{
    private readonly IMediator _mediator;

    public WhatsAppChatController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedList<ConversationDto>>> GetConversations(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null)
    {
        var result = await _mediator.Send(new GetConversationsQuery
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            Search = search
        });
        return Ok(result);
    }

    [HttpGet("{id:guid}/messages")]
    public async Task<ActionResult<PaginatedList<ChatMessageDto>>> GetMessages(
        Guid id,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _mediator.Send(new GetConversationMessagesQuery
        {
            ConversationId = id,
            PageNumber = pageNumber,
            PageSize = pageSize
        });

        if (result.IsFailure)
            return NotFound(new { error = result.Error });

        return Ok(result.Value);
    }

    [HttpPost("send")]
    public async Task<ActionResult<ChatMessageDto>> SendMessage([FromBody] SendChatMessageCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpPut("{id:guid}/read")]
    public async Task<IActionResult> MarkAsRead(Guid id)
    {
        var result = await _mediator.Send(new MarkConversationReadCommand(id));
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(new { message = "Conversación marcada como leída." });
    }

    /// <summary>Pauses (for a day) or resumes the automatic replies of one conversation.</summary>
    [HttpPut("{id:guid}/auto-replies")]
    public async Task<ActionResult<ConversationDto>> SetAutoReplies(Guid id, [FromBody] SetAutoRepliesRequest request)
    {
        var result = await _mediator.Send(new SetConversationAutoRepliesCommand(id, request.Paused));
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }
}

public record SetAutoRepliesRequest(bool Paused);
