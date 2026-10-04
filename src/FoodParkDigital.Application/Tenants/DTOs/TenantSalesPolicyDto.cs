using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Tenants.DTOs;

public class TenantSalesPolicyDto
{
    public bool PricesIncludeTax { get; set; }
    public decimal DefaultTaxRate { get; set; }
    public bool AutoCloseCounterSales { get; set; }
    public bool RequireOpenCashSession { get; set; }
    public OrderNotificationPolicy OrderNotificationPolicy { get; set; }
}
