using System.Text.Json.Serialization;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.DeliveryPlatforms;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.WebApi.Errors;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// The delivery platforms of the business (Configuración › Negocio › Plataformas de reparto). The rates of the contracts
/// are for owners and admins; whoever takes orders or reads the reports gets only which platforms are on. The prices of
/// the products in each app (Catálogo › Precios en apps) are for whoever changes the menu's prices.
/// </summary>
[ApiController]
[Route("api/v1/delivery-platforms")]
[Authorize]
public class DeliveryPlatformsController : ControllerBase
{
    private readonly IMediator _mediator;

    public DeliveryPlatformsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>The three platforms (Rappi, Uber Eats, DiDi Food) with their terms and an example of what each deposits.</summary>
    [HttpGet]
    public async Task<ActionResult<DeliveryPlatformsDto>> GetPlatforms(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetDeliveryPlatformsQuery(), cancellationToken));

    /// <summary>The platforms the business set up and whether each one takes orders, without their rates.</summary>
    [HttpGet("channels")]
    public async Task<ActionResult<List<DeliveryPlatformChannelDto>>> GetChannels(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetDeliveryPlatformChannelsQuery(), cancellationToken));

    /// <summary>
    /// Turns a platform on or off and saves its terms, with the revision the person saw (0 the first time); answers the
    /// three platforms. Someone else's save in between answers 409 <c>concurrency_conflict</c>.
    /// </summary>
    /// <param name="channel">Rappi, UberEats or DidiFood, by name or number.</param>
    [HttpPut("{channel}")]
    public async Task<ActionResult<DeliveryPlatformsDto>> SavePlatform(
        OrderChannel channel,
        [FromBody] SaveDeliveryPlatformRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SaveDeliveryPlatformCommand(
            channel,
            request.Revision,
            request.IsActive,
            request.CommissionPercent,
            request.CommissionBase,
            request.CommissionVatPercent,
            request.IsrWithholdingPercent,
            request.VatWithholdingPercent), cancellationToken);
        if (result.IsFailure)
        {
            // Someone saved the platform since it was loaded: like any concurrent save, the client reloads.
            return result.Error == DeliveryPlatformErrors.Stale
                ? this.ApiError(StatusCodes.Status409Conflict, result.Error, ApiErrorCodes.ConcurrencyConflict)
                : this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// The products (no services) with what each costs in the menu and in the platform's app, and what each of their
    /// options adds in both, by category and name.
    /// </summary>
    /// <param name="channel">Rappi, UberEats or DidiFood, by name or number.</param>
    [HttpGet("{channel}/prices")]
    public async Task<ActionResult<PaginatedList<PlatformPriceItemDto>>> GetPrices(
        OrderChannel channel,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(new GetPlatformPriceListQuery(channel, pageNumber, pageSize), cancellationToken));

    /// <summary>
    /// Saves prices in the platform's app: each product listed takes its price (null: the menu's) and each option listed
    /// what it adds (null: what it adds in the menu); what is left out stays as it is. Someone else's save of the same price
    /// at the same time answers 409 <c>concurrency_conflict</c>.
    /// </summary>
    /// <param name="channel">Rappi, UberEats or DidiFood, by name or number.</param>
    [HttpPut("{channel}/prices")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SavePrices(
        OrderChannel channel,
        [FromBody] SavePlatformPricesRequest request,
        CancellationToken cancellationToken)
    {
        // [JsonRequired] does not look inside a list: a null item gets here, and is refused like any other bad request.
        if (request.Products.Any(product => product is null || product.Options?.Any(option => option is null) == true))
            return this.ApiError(StatusCodes.Status400BadRequest, PlatformPriceErrors.EmptyItem, ApiErrorCodes.ValidationFailed);

        var result = await _mediator.Send(new SavePlatformPricesCommand(
            channel,
            request.Products
                .Select(product => new PlatformProductPriceInput(
                    product.ProductId,
                    product.Price,
                    product.Options?.Select(option => new PlatformOptionPriceInput(option.OptionId, option.PriceDelta)).ToList()))
                .ToList()), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error == DeliveryPlatformErrors.Stale
                ? this.ApiError(StatusCodes.Status409Conflict, result.Error, ApiErrorCodes.ConcurrencyConflict)
                : this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        }

        return NoContent();
    }
}

/// <summary>
/// A platform as it should be, with the revision the person saw (0 for one never saved). Every field is required: a
/// missing one is refused (400) instead of being read as 0, so no commission is saved that nobody wrote.
/// </summary>
public record SaveDeliveryPlatformRequest(
    [property: JsonRequired] int Revision,
    [property: JsonRequired] bool IsActive,
    [property: JsonRequired] decimal CommissionPercent,
    [property: JsonRequired] PlatformCommissionBase CommissionBase,
    [property: JsonRequired] decimal CommissionVatPercent,
    [property: JsonRequired] decimal IsrWithholdingPercent,
    [property: JsonRequired] decimal VatWithholdingPercent);

/// <summary>The prices in a platform's app that change, at most a page of the list at a time.</summary>
public record SavePlatformPricesRequest([property: JsonRequired] IReadOnlyList<PlatformProductPriceRequest> Products);

/// <summary>
/// A product's price in the app, always sent (null goes back to the menu's), so a price nobody wrote is never taken away;
/// its options are optional (left out, they keep their price in the app).
/// </summary>
public record PlatformProductPriceRequest(
    [property: JsonRequired] Guid ProductId,
    [property: JsonRequired] decimal? Price,
    IReadOnlyList<PlatformOptionPriceRequest>? Options);

/// <summary>What an option adds in the app, always sent (null goes back to what it adds in the menu).</summary>
public record PlatformOptionPriceRequest([property: JsonRequired] Guid OptionId, [property: JsonRequired] decimal? PriceDelta);
