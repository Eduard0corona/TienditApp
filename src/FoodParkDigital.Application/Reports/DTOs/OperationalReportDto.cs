namespace FoodParkDigital.Application.Reports.DTOs;

public class OperationalReportDto
{
    public DateTimeOffset RangeFrom { get; set; }
    public DateTimeOffset RangeTo { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalOrderPayments { get; set; }
    public decimal TotalPurchases { get; set; }
    public decimal TotalCogs { get; set; }
    public decimal GrossProfit { get; set; }
    public int OrdersCompleted { get; set; }
    public int AppointmentsCompleted { get; set; }
    public int AppointmentsScheduled { get; set; }
    public Dictionary<string, decimal> RevenueByPaymentMethod { get; set; } = [];
}
