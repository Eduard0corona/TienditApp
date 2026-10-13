using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Orders;
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
[Authorize]
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
        [FromQuery] OrderChannel? channel,
        [FromQuery] DateTimeOffset? fromDate,
        [FromQuery] DateTimeOffset? toDate,
        [FromQuery] string? search,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetOrdersByBusinessQuery
        {
            OrderTypeFilter = orderType,
            StatusFilter = status,
            ChannelFilter = channel,
            FromDate = fromDate,
            ToDate = toDate,
            Search = search,
            PageNumber = pageNumber,
            PageSize = pageSize
        }, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetOrderByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    /// <summary>Adds a round to an open tab and sends it to the kitchen.</summary>
    [HttpPost("{id:guid}/rounds")]
    public async Task<ActionResult<OrderDto>> AddRound(Guid id, [FromBody] AddTabRoundRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new AddTabRoundCommand { OrderId = id, Items = request.Items }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    /// <summary>Closes a paid tab and frees its table.</summary>
    [HttpPost("{id:guid}/close-tab")]
    public async Task<ActionResult<OrderDto>> CloseTab(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CloseTabCommand(id), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    /// <summary>
    /// Cancels units of a dish of an open tab already sent to the kitchen, with the reason: the tab stops charging them
    /// and their supplies go back to stock unless the kitchen prepared it. Only what is not paid yet.
    /// </summary>
    [HttpPost("{id:guid}/item-cancellations")]
    public async Task<ActionResult<OrderDto>> CancelTabItem(Guid id, [FromBody] CancelTabItemRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CancelTabItemCommand
        {
            OrderId = id,
            OrderItemId = request.OrderItemId,
            Quantity = request.Quantity,
            Reason = request.Reason,
            ReturnToStock = request.ReturnToStock
        }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    /// <summary>Moves an open tab to another free table.</summary>
    [HttpPut("{id:guid}/table")]
    public async Task<ActionResult<OrderDto>> MoveTab(Guid id, [FromBody] MoveTabRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new MoveTabCommand(id, request.DiningTableId), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    /// <summary>The waiter took a ready round to the table.</summary>
    [HttpPut("kitchen-tickets/{ticketId:guid}/served")]
    public async Task<IActionResult> MarkRoundServed(Guid ticketId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new MarkRoundServedCommand(ticketId), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status409Conflict, result.Error);
        return NoContent();
    }

    /// <summary>
    /// Previews the CFDI 4.0 invoice of the order: concepts, IVA per rate and what is missing to stamp it. Without
    /// receiver data the sale goes to the general public. Nothing is stamped.
    /// </summary>
    [HttpGet("{id:guid}/cfdi-draft")]
    public async Task<ActionResult<CfdiDraftDto>> GetCfdiDraft(
        Guid id,
        [FromQuery] string? rfc,
        [FromQuery] string? name,
        [FromQuery] string? postalCode,
        [FromQuery] string? fiscalRegime,
        [FromQuery] string? cfdiUse,
        CancellationToken cancellationToken)
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
        }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpPost]
    public async Task<ActionResult<OrderDto>> Create([FromBody] CreateOrderCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            // The same folio of the same platform again today: the client confirms and sends it again.
            return result.Error == PlatformOrderErrors.PossibleDuplicate
                ? this.ApiError(StatusCodes.Status409Conflict, result.Error, ApiErrorCodes.PossibleDuplicateOrder)
                : this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        }
        return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
    }

    // Whoever takes the orders; the kitchen moves food orders through KitchenController.
    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateOrderStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateOrderStatusCommand
        {
            OrderId = id,
            NewStatus = request.NewStatus
        }, cancellationToken);
        if (result.IsFailure)
        {
            if (result.Error!.Contains("otro proceso"))
                return this.ApiError(StatusCodes.Status409Conflict, result.Error);
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        }
        return NoContent();
    }

    /// <summary>Writes down (or corrects) the Paquetenvia trip of an order that goes to the customer's address.</summary>
    [HttpPut("{id:guid}/delivery/shipment")]
    public async Task<IActionResult> RegisterCourierShipment(
        Guid id, [FromBody] RegisterCourierShipmentRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new RegisterCourierShipmentCommand(id, request.Courier, request.Folio, request.TrackingUrl), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return NoContent();
    }

    [HttpPut("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelOrderRequest? request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CancelOrderCommand
        {
            OrderId = id,
            Reason = request?.Reason,
            RestockItems = request?.RestockItems ?? true
        }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return NoContent();
    }

    [HttpPost("{id:guid}/payments")]
    public async Task<ActionResult<OrderDto>> AddPayment(Guid id, [FromBody] AddOrderPaymentRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new AddOrderPaymentCommand
        {
            OrderId = id,
            Amount = request.Amount,
            Method = request.Method,
            InventoryLocationId = request.InventoryLocationId,
            EvidenceUrl = request.EvidenceUrl,
            Reference = request.Reference,
            Notes = request.Notes,
            Tip = request.Tip,
            CashReceived = request.CashReceived
        }, cancellationToken);

        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(result.Value);
    }

    /// <summary>Gives money back, optionally for returned units (owner, admin or ProcessRefunds).</summary>
    [HttpPost("{id:guid}/refunds")]
    public async Task<ActionResult<OrderDto>> Refund(Guid id, [FromBody] RefundOrderRequest request, CancellationToken cancellationToken)
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
        }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    /// <summary>Refunds what is left of every payment, returns the units and cancels when the state allows it.</summary>
    [HttpPost("{id:guid}/void")]
    public async Task<ActionResult<OrderDto>> Void(Guid id, [FromBody] VoidOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new VoidOrderCommand
        {
            OrderId = id,
            Reason = request.Reason,
            RestockItems = request.RestockItems
        }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpPut("{id:guid}/service-pieces/{pieceId:guid}/status")]
    public async Task<IActionResult> UpdateServicePieceStatus(
        Guid id,
        Guid pieceId,
        [FromBody] UpdateServiceOrderPieceStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateServiceOrderPieceStatusCommand
        {
            OrderId = id,
            PieceId = pieceId,
            NewStatus = request.NewStatus
        }, cancellationToken);

        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return NoContent();
    }

    [HttpPost("uploads/service-photo")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<UploadedFileDto>> UploadServicePhoto(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return this.ApiError(StatusCodes.Status400BadRequest, "No se proporcionó un archivo.");

        var result = await _mediator.Send(new UploadServiceIntakePhotoCommand
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

    [HttpPost("uploads/payment-evidence")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<UploadedFileDto>> UploadPaymentEvidence(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return this.ApiError(StatusCodes.Status400BadRequest, "No se proporcionó un archivo.");

        var result = await _mediator.Send(new UploadOrderPaymentEvidenceCommand
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
}

public record UpdateOrderStatusRequest
{
    public OrderStatus NewStatus { get; init; }
}

public record CancelOrderRequest
{
    public string? Reason { get; init; }

    /// <summary>Whether what the order consumed goes back to the stock; true when left out.</summary>
    public bool? RestockItems { get; init; }
}

public record AddOrderPaymentRequest
{
    public decimal Amount { get; init; }
    public OrderPaymentMethod Method { get; init; }
    public Guid? InventoryLocationId { get; init; }
    public string? EvidenceUrl { get; init; }
    public string? Reference { get; init; }
    public string? Notes { get; init; }
    /// <summary>On top of the amount, for the team: not part of the sale or its balance. 0 when not sent.</summary>
    [System.ComponentModel.DefaultValue(0)]
    public decimal Tip { get; init; }
    /// <summary>What the customer handed over in cash, to keep the change with the payment.</summary>
    public decimal? CashReceived { get; init; }
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

public class CancelTabItemRequest
{
    public Guid OrderItemId { get; init; }
    /// <summary>Up to three decimals for dishes sold by weight.</summary>
    public decimal Quantity { get; init; }
    public string Reason { get; init; } = string.Empty;
    /// <summary>The kitchen did not prepare it: its supplies go back to stock. Otherwise they were lost.</summary>
    public bool ReturnToStock { get; init; } = true;
}

public class RegisterCourierShipmentRequest
{
    public DeliveryCourier Courier { get; init; } = DeliveryCourier.Paquetenvia;
    /// <summary>The folio of the trip in the courier's app.</summary>
    public string Folio { get; init; } = string.Empty;
    /// <summary>The https link that follows the trip, if the courier gave one.</summary>
    public string? TrackingUrl { get; init; }
}
