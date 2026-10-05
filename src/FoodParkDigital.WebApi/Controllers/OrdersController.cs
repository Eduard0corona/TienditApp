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
        [FromQuery] string? search,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new GetOrdersByBusinessQuery
        {
            OrderTypeFilter = orderType,
            StatusFilter = status,
            FromDate = fromDate,
            ToDate = toDate,
            Search = search,
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

    /// <summary>Adds a round to an open tab and sends it to the kitchen.</summary>
    [HttpPost("{id:guid}/rounds")]
    [Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
    public async Task<ActionResult<OrderDto>> AddRound(Guid id, [FromBody] AddTabRoundRequest request)
    {
        var result = await _mediator.Send(new AddTabRoundCommand { OrderId = id, Items = request.Items });
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    /// <summary>Closes a paid tab and frees its table.</summary>
    [HttpPost("{id:guid}/close-tab")]
    [Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
    public async Task<ActionResult<OrderDto>> CloseTab(Guid id)
    {
        var result = await _mediator.Send(new CloseTabCommand(id));
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    /// <summary>Moves an open tab to another free table.</summary>
    [HttpPut("{id:guid}/table")]
    [Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
    public async Task<ActionResult<OrderDto>> MoveTab(Guid id, [FromBody] MoveTabRequest request)
    {
        var result = await _mediator.Send(new MoveTabCommand(id, request.DiningTableId));
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    /// <summary>The waiter took a ready round to the table.</summary>
    [HttpPut("kitchen-tickets/{ticketId:guid}/served")]
    [Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
    public async Task<IActionResult> MarkRoundServed(Guid ticketId)
    {
        var result = await _mediator.Send(new UpdateKitchenTicketStatusCommand(ticketId, KitchenTicketStatus.Served));
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status409Conflict, result.Error);
        return NoContent();
    }

    /// <summary>
    /// Previews the CFDI 4.0 invoice of the order: concepts, IVA per rate and what is missing to stamp it. Without
    /// receiver data the sale goes to the general public. Nothing is stamped.
    /// </summary>
    [HttpGet("{id:guid}/cfdi-draft")]
    [Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
    public async Task<ActionResult<CfdiDraftDto>> GetCfdiDraft(
        Guid id,
        [FromQuery] string? rfc,
        [FromQuery] string? name,
        [FromQuery] string? postalCode,
        [FromQuery] string? fiscalRegime,
        [FromQuery] string? cfdiUse)
    {
        var hasReceiver = !string.IsNullOrWhiteSpace(rfc) || !string.IsNullOrWhiteSpace(name)
            || !string.IsNullOrWhiteSpace(postalCode) || !string.IsNullOrWhiteSpace(fiscalRegime)
            || !string.IsNullOrWhiteSpace(cfdiUse);
        var result = await _mediator.Send(new GetOrderCfdiDraftQuery
        {
            OrderId = id,
            Receiver = hasReceiver
                ? new CfdiReceiverInput
                {
                    Rfc = rfc,
                    Name = name,
                    PostalCode = postalCode,
                    FiscalRegime = fiscalRegime,
                    CfdiUse = cfdiUse
                }
                : null
        });
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpPost]
    [Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
    public async Task<ActionResult<OrderDto>> Create([FromBody] CreateOrderCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
    }

    // Counter roles, like creating orders and registering payments; the middleware requires ManageOrders.
    // Kitchen moves food orders through KitchenController.
    [HttpPut("{id:guid}/status")]
    [Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
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
                return this.ApiError(StatusCodes.Status409Conflict, result.Error);
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        }
        return NoContent();
    }

    [HttpPut("{id:guid}/cancel")]
    [Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelOrderRequest? request)
    {
        var result = await _mediator.Send(new CancelOrderCommand
        {
            OrderId = id,
            Reason = request?.Reason
        });
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
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
            InventoryLocationId = request.InventoryLocationId,
            EvidenceUrl = request.EvidenceUrl,
            Reference = request.Reference,
            Notes = request.Notes
        });

        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(result.Value);
    }

    /// <summary>Gives money back, optionally for returned units (owner, admin or ProcessRefunds).</summary>
    [HttpPost("{id:guid}/refunds")]
    [Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
    public async Task<ActionResult<OrderDto>> Refund(Guid id, [FromBody] RefundOrderRequest request)
    {
        var result = await _mediator.Send(new RefundOrderCommand
        {
            OrderId = id,
            Amount = request.Amount,
            Method = request.Method,
            Reason = request.Reason,
            RestockItems = request.RestockItems,
            Items = request.Items,
            InventoryLocationId = request.InventoryLocationId,
            Reference = request.Reference
        });
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    /// <summary>Refunds what is left of every payment, returns the units and cancels when the state allows it.</summary>
    [HttpPost("{id:guid}/void")]
    [Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
    public async Task<ActionResult<OrderDto>> Void(Guid id, [FromBody] VoidOrderRequest request)
    {
        var result = await _mediator.Send(new VoidOrderCommand
        {
            OrderId = id,
            Reason = request.Reason,
            RestockItems = request.RestockItems
        });
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
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
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return NoContent();
    }

    [HttpPost("uploads/service-photo")]
    [Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<UploadedFileDto>> UploadServicePhoto(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return this.ApiError(StatusCodes.Status400BadRequest, "No se proporcionó un archivo.");

        var result = await _mediator.Send(new UploadServiceIntakePhotoCommand
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

    [HttpPost("uploads/payment-evidence")]
    [Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<UploadedFileDto>> UploadPaymentEvidence(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return this.ApiError(StatusCodes.Status400BadRequest, "No se proporcionó un archivo.");

        var result = await _mediator.Send(new UploadOrderPaymentEvidenceCommand
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
    public Guid? InventoryLocationId { get; init; }
    public string? EvidenceUrl { get; init; }
    public string? Reference { get; init; }
    public string? Notes { get; init; }
}

public record UpdateServiceOrderPieceStatusRequest
{
    public ServiceOrderPieceStatus NewStatus { get; init; }
}

public class RefundOrderRequest
{
    public decimal Amount { get; init; }
    public OrderPaymentMethod Method { get; init; }
    public string Reason { get; init; } = string.Empty;
    public bool RestockItems { get; init; } = true;
    public List<RefundItemDto> Items { get; init; } = [];
    public Guid? InventoryLocationId { get; init; }
    public string? Reference { get; init; }
}

public class VoidOrderRequest
{
    public string Reason { get; init; } = string.Empty;
    public bool RestockItems { get; init; } = true;
}

public class AddTabRoundRequest
{
    public List<CreateOrderItemDto> Items { get; init; } = [];
}

public class MoveTabRequest
{
    public Guid DiningTableId { get; init; }
}
