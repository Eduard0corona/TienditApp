using FoodParkDigital.Application.Appointments.OnlineBooking;
using FoodParkDigital.Application.OnlineBooking;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.OnlinePayments;
using FoodParkDigital.Application.Reviews;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// The public menu of a business (/m/{slug} in the app): no sign-in, only what the business published, and
/// orders only when it turned them on. Rate limited per IP. A customer account that sends its session orders with it
/// (Fase 24B). Its booking page (/m/{slug}/agendar, Fase 24D) asks for appointments the same way.
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

    /// <summary>
    /// The published reviews of the store (Fase 24H), newest first, ten at a time, with how its customers rate it; never
    /// who wrote them beyond «Mariana O.».
    /// </summary>
    /// <param name="slug">The business of the menu.</param>
    /// <param name="page">From 1.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    [HttpGet("reviews")]
    [EnableRateLimiting("public-menu")]
    public async Task<ActionResult<PublicReviewsDto>> GetReviews(string slug, [FromQuery] int page = 1, CancellationToken cancellationToken = default)
    {
        var reviews = await _mediator.Send(new GetPublicReviewsQuery(slug, page), cancellationToken);
        return reviews is null ? this.ApiError(StatusCodes.Status404NotFound, ReviewRules.StoreNotFoundMessage) : Ok(reviews);
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

    /// <summary>The services a customer books online with the business (Fase 24D); not found when it does not book online.</summary>
    /// <param name="slug">The business of the menu.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    [HttpGet("booking")]
    [EnableRateLimiting("public-menu")]
    public async Task<ActionResult<PublicBookingDto>> GetBooking(string slug, CancellationToken cancellationToken)
    {
        var booking = await _mediator.Send(new GetPublicBookingQuery(slug), cancellationToken);
        return booking is null ? this.ApiError(StatusCodes.Status404NotFound, OnlineBookingAvailability.NotBookingMessage) : Ok(booking);
    }

    /// <summary>The free times of a service, a week from <paramref name="from"/> (today when omitted), in the business's time.</summary>
    /// <param name="slug">The business of the menu.</param>
    /// <param name="serviceId">A service the business books online.</param>
    /// <param name="from">The first day of the week asked for.</param>
    /// <param name="staffId">The person chosen (Fase 24J); none for whoever is free.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    [HttpGet("booking/slots")]
    [EnableRateLimiting("public-menu")]
    public async Task<ActionResult<PublicBookingSlotsDto>> GetBookingSlots(
        string slug, [FromQuery] Guid serviceId, [FromQuery] DateOnly? from, [FromQuery] Guid? staffId, CancellationToken cancellationToken)
    {
        var slots = await _mediator.Send(new GetPublicBookingSlotsQuery(slug, serviceId, from, staffId), cancellationToken);
        return slots is null
            ? this.ApiError(StatusCodes.Status404NotFound, RequestPublicAppointmentCommandHandler.ServiceNotBookedMessage)
            : Ok(slots);
    }

    /// <summary>Asks for an appointment at one of the free times (Fase 24D): it waits for the business to accept it.</summary>
    [HttpPost("appointments")]
    [EnableRateLimiting("public-order")]
    public async Task<ActionResult<PublicAppointmentReceiptDto>> RequestAppointment(
        string slug, [FromBody] RequestPublicAppointmentRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RequestPublicAppointmentCommand
        {
            Slug = slug,
            ServiceProductId = request.ServiceProductId,
            StartAt = request.StartAt,
            CustomerName = request.CustomerName,
            CustomerPhone = request.CustomerPhone,
            Notes = request.Notes,
            IdempotencyKey = request.IdempotencyKey,
            StaffId = request.StaffId
        }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
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

    /// <summary>
    /// Pays the order online (Fase 24I): the server prices it and answers Mercado Pago's page, where the customer pays into
    /// the business's account within 30 minutes. The order is placed when the payment is approved.
    /// </summary>
    [HttpPost("checkouts")]
    [EnableRateLimiting("public-order")]
    public async Task<ActionResult<OnlineCheckoutStartedDto>> CreateCheckout(
        string slug, [FromBody] CreateOnlineCheckoutRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CreateOnlineCheckoutCommand
        {
            Slug = slug,
            CustomerName = request.CustomerName,
            CustomerPhone = request.CustomerPhone,
            Notes = request.Notes,
            Items = request.Items,
            ExpectedTotal = request.ExpectedTotal,
            Delivery = request.Delivery,
            SaveAddress = request.SaveAddress,
            AddressLabel = request.AddressLabel
        }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    /// <summary>
    /// How the online payment stands, after asking Mercado Pago (the customer's return page): to pay, its order placed, or
    /// paid but not placed and going back. 404 for a payment that is not of this business.
    /// </summary>
    [HttpPost("checkouts/{checkoutId:guid}/confirm")]
    [EnableRateLimiting("online-payment-status")]
    public async Task<ActionResult<OnlineCheckoutStatusDto>> ConfirmCheckout(
        string slug, Guid checkoutId, [FromBody] ConfirmOnlineCheckoutRequest? request, CancellationToken cancellationToken)
    {
        var status = await _mediator.Send(new ConfirmOnlineCheckoutCommand(slug, checkoutId, request?.PaymentId, request?.ReturnKey), cancellationToken);
        return status is null ? this.ApiError(StatusCodes.Status404NotFound, "No encontramos ese pago.") : Ok(status);
    }
}

public class CreateOnlineCheckoutRequest
{
    public string CustomerName { get; init; } = null!;
    public string CustomerPhone { get; init; } = null!;
    public string? Notes { get; init; }
    public List<FoodParkDigital.Application.Orders.Commands.CreateOrderItemDto> Items { get; init; } = [];
    public decimal? ExpectedTotal { get; init; }
    /// <summary>Where the order goes; none to pick it up. Paid online, it carries no bill for the courier.</summary>
    public FoodParkDigital.Application.Orders.OrderDeliveryInput? Delivery { get; init; }
    public bool SaveAddress { get; init; }
    public string? AddressLabel { get; init; }
}

public class ConfirmOnlineCheckoutRequest
{
    /// <summary>The <c>payment_id</c> Mercado Pago's return carries, if any.</summary>
    public string? PaymentId { get; init; }
    /// <summary>The return key the store kept when the payment started; without it the tracking link is not answered.</summary>
    public string? ReturnKey { get; init; }
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

public class RequestPublicAppointmentRequest
{
    public Guid ServiceProductId { get; init; }
    public DateTimeOffset StartAt { get; init; }
    public string CustomerName { get; init; } = null!;
    public string CustomerPhone { get; init; } = null!;
    public string? Notes { get; init; }
    /// <summary>The booking page's key for this request: sent again, it answers the appointment already requested.</summary>
    public Guid? IdempotencyKey { get; init; }
    /// <summary>The person chosen (Fase 24J); none for whoever is free.</summary>
    public Guid? StaffId { get; init; }
}
