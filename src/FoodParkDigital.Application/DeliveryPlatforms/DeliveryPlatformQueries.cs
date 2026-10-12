using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Orders.Taxes;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.DeliveryPlatforms;

/// <summary>
/// Configuración › Negocio › Plataformas de reparto (owners and admins): the three platforms with their terms and an
/// example of what each one deposits. Only they see the rates of the contracts.
/// </summary>
[RequiresTenantManager]
public record GetDeliveryPlatformsQuery : IRequest<DeliveryPlatformsDto>;

/// <summary>
/// The platforms the business set up and whether each one takes orders, without their rates: what Vender and Pedidos
/// need (and Reportes, to filter by platform).
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders, TenantPermission.ViewReports)]
public record GetDeliveryPlatformChannelsQuery : IRequest<List<DeliveryPlatformChannelDto>>;

public class GetDeliveryPlatformsQueryHandler : IRequestHandler<GetDeliveryPlatformsQuery, DeliveryPlatformsDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetDeliveryPlatformsQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public Task<DeliveryPlatformsDto> Handle(GetDeliveryPlatformsQuery request, CancellationToken cancellationToken)
        => DeliveryPlatformsReader.LoadAsync(_context, _tenantService.TenantId, cancellationToken);
}

public class GetDeliveryPlatformChannelsQueryHandler : IRequestHandler<GetDeliveryPlatformChannelsQuery, List<DeliveryPlatformChannelDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetDeliveryPlatformChannelsQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<DeliveryPlatformChannelDto>> Handle(GetDeliveryPlatformChannelsQuery request, CancellationToken cancellationToken)
        => await _context.DeliveryPlatformSettings
            .AsNoTracking()
            .Where(x => x.TenantId == _tenantService.TenantId)
            .OrderBy(x => x.Channel)
            .Select(x => new DeliveryPlatformChannelDto { Channel = x.Channel, IsActive = x.IsActive })
            .ToListAsync(cancellationToken);
}

/// <summary>The list of the screen, as the query and every save answer it.</summary>
internal static class DeliveryPlatformsReader
{
    /// <summary>The order of the example: $1,000 with IVA at 16 % included, as in DiDi's published example.</summary>
    public const decimal ExampleOrderTotal = 1000m;

    /// <summary>
    /// One row per delivery platform, from the enum: there are three at most per business (the check of the channel and
    /// the unique index), and the ones never saved read with the defaults.
    /// </summary>
    public static async Task<DeliveryPlatformsDto> LoadAsync(IApplicationDbContext context, Guid tenantId, CancellationToken cancellationToken)
    {
        var saved = await context.DeliveryPlatformSettings
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        return new DeliveryPlatformsDto
        {
            Platforms = OrderChannelRules.DeliveryPlatforms
                .Select(channel => ToDto(channel, saved.FirstOrDefault(x => x.Channel == channel)))
                .ToList()
        };
    }

    private static DeliveryPlatformDto ToDto(OrderChannel channel, DeliveryPlatformSetting? setting)
    {
        if (setting is null)
        {
            return new DeliveryPlatformDto
            {
                Channel = channel,
                CommissionBase = PlatformTerms.DefaultCommissionBase,
                CommissionVatPercent = PlatformTerms.DefaultCommissionVatPercent,
                IsrWithholdingPercent = PlatformTerms.DefaultIsrWithholdingPercent,
                VatWithholdingPercent = PlatformTerms.DefaultVatWithholdingPercent
            };
        }

        var updated = setting.UpdatedAt is not null;
        return new DeliveryPlatformDto
        {
            Channel = setting.Channel,
            IsConfigured = true,
            IsActive = setting.IsActive,
            CommissionPercent = setting.CommissionPercent,
            CommissionBase = setting.CommissionBase,
            CommissionVatPercent = setting.CommissionVatPercent,
            IsrWithholdingPercent = setting.IsrWithholdingPercent,
            VatWithholdingPercent = setting.VatWithholdingPercent,
            Revision = setting.Revision,
            ChangedAt = updated ? setting.UpdatedAt : setting.CreatedAt,
            ChangedBy = updated ? setting.UpdatedBy : setting.CreatedBy,
            Example = Example(setting)
        };
    }

    /// <summary>What the terms give on the example order charged by the platform, calculated by the domain.</summary>
    private static PlatformSettlementExampleDto? Example(DeliveryPlatformSetting setting)
    {
        PlatformTerms terms;
        try
        {
            terms = setting.Terms;
        }
        catch (DomainException)
        {
            // Stored terms outside today's limits (written before they changed): the row still shows, to be fixed.
            return null;
        }

        var taxLines = OrderTaxCalculator.Calculate(pricesIncludeTax: true, [new TaxableAmount(ExampleOrderTotal, VatRate.General)]);
        var subTotal = taxLines.Sum(line => line.TaxableBase);
        var total = taxLines.Sum(line => line.Total);
        var amounts = PlatformOrderCalculator.Calculate(terms, PlatformCollectedBy.Platform, subTotal, total, taxLines);
        return new PlatformSettlementExampleDto
        {
            OrderTotal = total,
            CommissionAndTax = amounts.CommissionAndTax,
            Withheld = amounts.Withheld,
            ExpectedSettlement = amounts.ExpectedSettlement
        };
    }
}
