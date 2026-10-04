namespace FoodParkDigital.Application.Tenants.DTOs;

public class TenantSalesPolicyDto
{
    public bool PricesIncludeTax { get; set; }
    public decimal DefaultTaxRate { get; set; }
    public bool AutoCloseCounterSales { get; set; }
}
