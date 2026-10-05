using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Common.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders;

/// <summary>The settings of a business that decide how a sale is priced and closed.</summary>
public sealed record TenantSalesSettings(
    string? TimeZone,
    bool PricesIncludeTax,
    decimal DefaultTaxRate,
    bool AutoCloseCounterSales,
    bool RequireOpenCashSession = false)
{
    /// <summary>
    /// A business without settings sells as every business did before tax policies: 16 % added on top.
    /// </summary>
    public static readonly TenantSalesSettings Legacy = new(null, false, VatRate.GeneralPercent, true);

    public static async Task<TenantSalesSettings> ReadAsync(
        IApplicationDbContext context,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var settings = await context.TenantSettings
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => new { x.TimeZone, x.PricesIncludeTax, x.DefaultTaxRate, x.AutoCloseCounterSales, x.RequireOpenCashSession })
            .FirstOrDefaultAsync(cancellationToken);

        return settings is null
            ? Legacy
            : new TenantSalesSettings(
                settings.TimeZone,
                settings.PricesIncludeTax,
                settings.DefaultTaxRate,
                settings.AutoCloseCounterSales,
                settings.RequireOpenCashSession);
    }
}
