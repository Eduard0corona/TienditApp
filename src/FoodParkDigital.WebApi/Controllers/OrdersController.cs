using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Orders.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF,KITCHEN")]
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrdersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedList<OrderDto>>> GetAll(
        [FromQuery] OrderType? orderType,
        [FromQuery] OrderStatus? status,
        [FromQuery] DateTimeOffset? fromDate,
        [FromQuery] DateTimeOffset? toDate,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new GetOrdersByBusinessQuery
        {
            OrderTypeFilter = orderType,
            StatusFilter = status,
            FromDate = fromDate,
            ToDate = toDate,
            PageNumber = pageNumber,
            PageSize = pageSize
        });
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> GetById(Guid id)
    {
        var result = await _mediator.Send(new GetOrderByIdQuery(id));
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
    public async Task<ActionResult<OrderDto>> Create([FromBody] CreateOrderCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
    }

    [HttpPut("{id:guid}/status")]
    [Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateOrderStatusRequest request)
    {
        var result = await _mediator.Send(new UpdateOrderStatusCommand
        {
            OrderId = id,
            NewStatus = request.NewStatus
        });
        if (result.IsFailure)
        {
            if (result.Error!.Contains("otro proceso"))
                return Conflict(new { error = result.Error });
            return BadRequest(new { error = result.Error });
        }
        return NoContent();
    }

    [HttpPut("{id:guid}/cancel")]
    [Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelOrderRequest? request)
    {
        var result = await _mediator.Send(new CancelOrderCommand
        {
            OrderId = id,
            Reason = request?.Reason
        });
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return NoContent();
    }

    [HttpPost("{id:guid}/payments")]
    [Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
    public async Task<ActionResult<OrderDto>> AddPayment(Guid id, [FromBody] AddOrderPaymentRequest request)
    {
        var result = await _mediator.Send(new AddOrderPaymentCommand
        {
            OrderId = id,
            Amount = request.Amount,
            Method = request.Method,
            PaidAt = request.PaidAt,
            InventoryLocationId = request.InventoryLocationId,
            EvidenceUrl = request.EvidenceUrl,
            Reference = request.Reference,
            Notes = request.Notes
        });

        if (result.IsFailure)
            return BadRequest(new { error = result.Error });

        return Ok(result.Value);
    }

    [HttpPut("{id:guid}/service-pieces/{pieceId:guid}/status")]
    [Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
    public async Task<IActionResult> UpdateServicePieceStatus(
        Guid id,
        Guid pieceId,
        [FromBody] UpdateServiceOrderPieceStatusRequest request)
    {
        var result = await _mediator.Send(new UpdateServiceOrderPieceStatusCommand
        {
            OrderId = id,
            PieceId = pieceId,
            NewStatus = request.NewStatus
        });

        if (result.IsFailure)
            return BadRequest(new { error = result.Error });

        return NoContent();
    }

    [HttpPost("uploads/service-photo")]
    [Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<UploadedFileDto>> UploadServicePhoto(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No se proporciono un archivo." });

        var result = await _mediator.Send(new UploadServiceIntakePhotoCommand
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

    [HttpPost("uploads/payment-evidence")]
    [Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<UploadedFileDto>> UploadPaymentEvidence(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No se proporciono un archivo." });

        var result = await _mediator.Send(new UploadOrderPaymentEvidenceCommand
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
}

public record UpdateOrderStatusRequest
{
    public OrderStatus NewStatus { get; init; }
}

public record CancelOrderRequest
{
    public string? Reason { get; init; }
}

public record AddOrderPaymentRequest
{
    public decimal Amount { get; init; }
    public OrderPaymentMethod Method { get; init; }
    public DateTimeOffset? PaidAt { get; init; }
    public Guid? InventoryLocationId { get; init; }
    public string? EvidenceUrl { get; init; }
    public string? Reference { get; init; }
    public string? Notes { get; init; }
}

public record UpdateServiceOrderPieceStatusRequest
{
    public ServiceOrderPieceStatus NewStatus { get; init; }
}
