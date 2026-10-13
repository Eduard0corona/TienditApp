using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders;

/// <summary>
/// Where a new order goes (Fase 22C), as the customer writes it in the online menu or the team takes it in Vender: the
/// postal code its delivery zone is found by, the address and, optionally, references to find the place.
/// </summary>
public record OrderDeliveryInput
{
    public string PostalCode { get; init; } = string.Empty;
    public string AddressLine { get; init; } = string.Empty;
    public string? References { get; init; }
}

public class OrderDeliveryInputValidator : AbstractValidator<OrderDeliveryInput>
{
    /// <summary>The longest postal code read: something longer is not one, whatever it has around.</summary>
    public const int MaxPostalCodeLength = 20;

    public OrderDeliveryInputValidator()
    {
        RuleFor(x => x.PostalCode).Cascade(CascadeMode.Stop).NotEmpty().WithMessage(DeliveryAddress.PostalCodeRequiredMessage)
            // Without what was written: a validation message reaches the log.
            .MaximumLength(MaxPostalCodeLength).WithMessage("El código postal lleva 5 dígitos.");
        RuleFor(x => x.AddressLine).Cascade(CascadeMode.Stop).NotEmpty().WithMessage(DeliveryAddress.AddressLineRequiredMessage)
            .Must(line => line.Trim().Length <= DeliveryAddress.MaxAddressLineLength).WithMessage(DeliveryAddress.AddressLineTooLongMessage);
        RuleFor(x => x.References)
            .Must(references => references is null || references.Trim().Length <= DeliveryAddress.MaxReferencesLength)
            .WithMessage(DeliveryAddress.ReferencesTooLongMessage);
    }
}

/// <summary>
/// Sends a new order to an address with the active zone of its business that reaches the postal code: the order checks
/// the zone's minimum and adds what the zone charges (<see cref="Order.ArrangeDelivery"/>). The same for the online menu
/// and for Vender.
/// </summary>
internal static class HomeDeliveryPlacement
{
    /// <summary>
    /// The active zone of the business that reaches <paramref name="postalCode"/>, with that postal code only, or null. A
    /// postal code is in one zone of the business at most (IX_DeliveryZonePostalCodes_TenantId_PostalCode).
    /// </summary>
    public static Task<DeliveryZone?> FindZoneAsync(
        IApplicationDbContext context, Guid tenantId, string postalCode, CancellationToken cancellationToken)
        => context.DeliveryZones
            .AsNoTracking()
            .Include(zone => zone.PostalCodes.Where(code => code.PostalCode == postalCode))
            .Where(zone => zone.TenantId == tenantId && zone.IsActive && zone.PostalCodes.Any(code => code.PostalCode == postalCode))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Arranges the delivery of <paramref name="order"/>, after its products and before any payment or change of status;
    /// the message for the customer or the team when it cannot go there, or null.
    /// </summary>
    /// <param name="feeTaxRate">The IVA rate of the business (TenantSalesSettings.DefaultTaxRate).</param>
    public static async Task<string?> ArrangeAsync(
        IApplicationDbContext context, Order order, OrderDeliveryInput input, decimal feeTaxRate, CancellationToken cancellationToken)
    {
        try
        {
            var address = DeliveryAddress.From(input.PostalCode, input.AddressLine, input.References);
            var zone = await FindZoneAsync(context, order.TenantId, address.PostalCode, cancellationToken);
            if (zone is null)
                return HomeDeliveryRules.NotCoveredMessage(address.PostalCode);

            order.ArrangeDelivery(address, zone, VatRate.Of(feeTaxRate));
            return null;
        }
        catch (DomainException ex)
        {
            return ex.Message;
        }
    }
}
