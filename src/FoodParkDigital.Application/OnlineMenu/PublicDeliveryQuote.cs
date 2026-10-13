using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Delivery;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;

namespace FoodParkDigital.Application.OnlineMenu;

/// <summary>
/// Whether a business delivers to a postal code, for its online menu (Fase 22C): what the delivery costs, the smallest
/// order it takes and from what order it is free. Never the zone's name, which can be an internal one.
/// </summary>
public class PublicDeliveryQuoteDto
{
    public string PostalCode { get; set; } = null!;
    public bool Covered { get; set; }
    /// <summary>What the delivery costs, with the business's tax policy like its prices; null when it does not deliver there.</summary>
    public decimal? Fee { get; set; }
    /// <summary>The smallest order in products; 0 is no minimum. Null when it does not deliver there.</summary>
    public decimal? MinimumOrder { get; set; }
    /// <summary>From what order in products the delivery is free, when the zone has it.</summary>
    public decimal? FreeDeliveryFrom { get; set; }
}

/// <summary>The menu's postal code check: null when the business has no online menu.</summary>
[AllowAnonymousRequest("Envío del menú público: si el negocio llega a un código postal y cuánto cobra, sin el nombre de la zona.")]
public record GetPublicDeliveryQuoteQuery(string Slug, string PostalCode) : IRequest<PublicDeliveryQuoteDto?>;

public class GetPublicDeliveryQuoteQueryValidator : AbstractValidator<GetPublicDeliveryQuoteQuery>
{
    public const string PostalCodeMessage = "El código postal lleva 5 dígitos.";

    public GetPublicDeliveryQuoteQueryValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PostalCode).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Escribe tu código postal.")
            .Must(code => DeliveryZone.IsPostalCode(DeliveryZone.NormalizePostalCode(code)))
            // Without what was written: a validation message reaches the log (ADR 0017 keeps postal codes out of it).
            .WithMessage(PostalCodeMessage);
    }
}

public class GetPublicDeliveryQuoteQueryHandler : IRequestHandler<GetPublicDeliveryQuoteQuery, PublicDeliveryQuoteDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetPublicDeliveryQuoteQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<PublicDeliveryQuoteDto?> Handle(GetPublicDeliveryQuoteQuery request, CancellationToken cancellationToken)
    {
        var tenant = await PublicMenuTenant.FindAsync(_context, request.Slug, cancellationToken);
        if (tenant is null || tenant.Settings!.OnlineMenuMode == OnlineMenuMode.Off)
            return null;

        // A public request has no tenant: the zones read below are the business of the slug's.
        _tenantService.SetTenant(tenant.Id);
        var postalCode = DeliveryZone.NormalizePostalCode(request.PostalCode)!;
        var zone = await DeliveryZoneLookup.FindAsync(_context, tenant.Id, postalCode, cancellationToken);
        return new PublicDeliveryQuoteDto
        {
            PostalCode = postalCode,
            Covered = zone is not null,
            Fee = zone?.Fee,
            MinimumOrder = zone?.MinimumOrder,
            FreeDeliveryFrom = zone?.FreeDeliveryFrom
        };
    }
}
