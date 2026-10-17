using FoodParkDigital.Application.OnlineMenu;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// The public menu of a business (/m/{slug} in the app): no sign-in, only what the business published, and
/// orders only when it turned them on. Rate limited per IP. A customer account that sends its session orders with it
/// (Fase 24B).
/// </summary>
[ApiController]
[Route("api/v1/public/menu/{slug}")]
[AllowAnonymous]
public class PublicMenuController : ControllerBase
{
    private readonly IMediator _mediator;

    public PublicMenuController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <param name="slug">The business of the menu.</param>
    /// <param name="table">The table of the QR it was opened from: at a table of the business, orders arrive outside the hours.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    [HttpGet]
    [EnableRateLimiting("public-menu")]
    public async Task<ActionResult<PublicMenuDto>> GetMenu(string slug, [FromQuery] string? table, CancellationToken cancellationToken)
    {
        var menu = await _mediator.Send(new GetPublicMenuQuery(slug, table), cancellationToken);
        return menu is null ? this.ApiError(StatusCodes.Status404NotFound, "Este negocio no tiene menú en línea.") : Ok(menu);
    }

    /// <summary>Whether the business delivers to a postal code and what it charges (Fase 22C); never the zone's name.</summary>
    /// <param name="slug">The business of the menu.</param>
    /// <param name="postalCode">Five digits.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    [HttpGet("delivery-quote")]
    [EnableRateLimiting("public-menu")]
    public async Task<ActionResult<PublicDeliveryQuoteDto>> GetDeliveryQuote(
        string slug, [FromQuery] string postalCode, CancellationToken cancellationToken)
    {
        var quote = await _mediator.Send(new GetPublicDeliveryQuoteQuery(slug, postalCode), cancellationToken);
        return quote is null ? this.ApiError(StatusCodes.Status404NotFound, "Este negocio no tiene menú en línea.") : Ok(quote);
    }

    [HttpPost("orders")]
    [EnableRateLimiting("public-order")]
    public async Task<ActionResult<PublicOrderReceiptDto>> PlaceOrder(string slug, [FromBody] PlacePublicOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new PlacePublicOrderCommand
        {
            Slug = slug,
            CustomerName = request.CustomerName,
            CustomerPhone = request.CustomerPhone,
            Notes = request.Notes,
            TableLabel = request.TableLabel,
            Items = request.Items,
            ExpectedTotal = request.ExpectedTotal,
            Delivery = request.Delivery,
            IdempotencyKey = request.IdempotencyKey,
            SaveAddress = request.SaveAddress,
            AddressLabel = request.AddressLabel
        }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }
}

public class PlacePublicOrderRequest
{
    public string CustomerName { get; init; } = null!;
    public string CustomerPhone { get; init; } = null!;
    public string? Notes { get; init; }
    public string? TableLabel { get; init; }
    public List<FoodParkDigital.Application.Orders.Commands.CreateOrderItemDto> Items { get; init; } = [];
    public decimal? ExpectedTotal { get; init; }
    /// <summary>Where the order goes (Fase 22C); none to pick it up or for a table.</summary>
    public FoodParkDigital.Application.Orders.OrderDeliveryInput? Delivery { get; init; }
    /// <summary>The store's key for this order (Fase 24B): sent again, it answers the order already placed.</summary>
    public Guid? IdempotencyKey { get; init; }
    /// <summary>Keep the delivery address in the signed-in customer account, named <see cref="AddressLabel"/> (Fase 24B).</summary>
    public bool SaveAddress { get; init; }
    public string? AddressLabel { get; init; }
}
