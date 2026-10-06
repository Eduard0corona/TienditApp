using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Orders.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/service-board")]
[Authorize]
public class ServiceBoardController : ControllerBase
{
    private readonly IMediator _mediator;

    public ServiceBoardController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("pieces")]
    public async Task<ActionResult<PaginatedList<ServiceBoardPieceDto>>> GetPieces(
        [FromQuery] string? search,
        [FromQuery] ServiceOrderPieceStatus? status,
        [FromQuery] bool includeCompleted = false,
        [FromQuery] bool includeCancelled = false,
        [FromQuery] bool onlyMine = false,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetServiceBoardPiecesQuery
        {
            Search = search,
            Status = status,
            IncludeCompleted = includeCompleted,
            IncludeCancelled = includeCancelled,
            OnlyMine = onlyMine,
            PageNumber = pageNumber,
            PageSize = pageSize
        }, cancellationToken);

        return Ok(result);
    }

    [HttpPut("pieces/{pieceId:guid}/assign-to-me")]
    public async Task<IActionResult> AssignToMe(Guid pieceId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new AssignServiceOrderPieceToMeCommand
        {
            PieceId = pieceId
        }, cancellationToken);

        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return NoContent();
    }

    [HttpPut("pieces/{pieceId:guid}/release")]
    public async Task<IActionResult> Release(Guid pieceId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ReleaseServiceOrderPieceAssignmentCommand
        {
            PieceId = pieceId
        }, cancellationToken);

        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return NoContent();
    }
}
