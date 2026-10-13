using System.Net;
using System.Net.Http.Json;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Charging an order (Pedidos › Cobrar, or a table's tab) sends the tip and the cash the customer handed over with each
/// payment. Through the API and SQL Server: both stay with the payment, with its change, and the tip enters the drawer
/// as its own movement, apart from the sale.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class OrderPaymentTipSqlServerTests
{
    private readonly TwoBusinessesApi _api;

    public OrderPaymentTipSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task CashPayment_WithTipAndCashReceived_ShouldKeepBoth_AndPutTheTipInTheDrawer()
    {
        var shop = await _api.SignUpAsync("propina");
        var client = shop.Client;
        var product = await ProductAsync(client, "Agua");
        await TwoBusinessesApi.IdAsync(client, "/api/v1/cash/open", new { openingAmount = 0m });
        var order = await TwoBusinessesApi.ReadAsync(await client.PostAsJsonAsync("/api/v1/orders", new
        {
            orderType = 0,
            channel = 0,
            createdByBusiness = true,
            customerName = "Mostrador",
            items = new[] { Line(product) },
            payments = Array.Empty<object>()
        }));
        var orderId = order.GetProperty("id").GetGuid();
        var total = order.GetProperty("total").GetDecimal();

        // Paid in cash as Cobrar sends it: the total, a tip of 5 (never above the total) and a bill that leaves 30 of
        // change.
        var paid = await TwoBusinessesApi.ReadAsync(await client.PostAsJsonAsync($"/api/v1/orders/{orderId}/payments", new
        {
            amount = total,
            method = 0,
            tip = 5m,
            cashReceived = total + 35m
        }));

        var payment = Assert.Single(paid.GetProperty("payments").EnumerateArray());
        Assert.Equal(5m, payment.GetProperty("tip").GetDecimal());
        Assert.Equal(total + 35m, payment.GetProperty("cashReceived").GetDecimal());
        Assert.Equal(30m, payment.GetProperty("change").GetDecimal());
        // The tip is not part of the sale: the total alone settles the order.
        Assert.Equal(0m, paid.GetProperty("balance").GetDecimal());

        var stored = await _api.QueryAsync(shop.TenantId, db => db.OrderPayments.AsNoTracking().SingleAsync(p => p.OrderId == orderId));
        Assert.Equal(5m, stored.Tip);
        Assert.Equal(total + 35m, stored.CashReceived);

        // The drawer takes the sale and, as its own movement, the tip.
        var movements = await _api.QueryAsync(shop.TenantId, db => db.CashTransactions.AsNoTracking()
            .Where(t => t.OrderId == orderId)
            .OrderBy(t => t.TransactionType)
            .Select(t => new { t.TransactionType, t.Amount, t.PaymentMethod })
            .ToListAsync());
        Assert.Equal(
            [(CashTransactionType.SalePayment, total), (CashTransactionType.Tip, 5m)],
            movements.Select(m => (m.TransactionType, m.Amount)));
        Assert.All(movements, m => Assert.Equal(OrderPaymentMethod.Cash, m.PaymentMethod));

        // Cash received belongs to cash payments: a card payment that carries it is refused.
        var card = await client.PostAsJsonAsync($"/api/v1/orders/{orderId}/payments", new { amount = 1m, method = 1, cashReceived = 1m });
        Assert.Equal(HttpStatusCode.BadRequest, card.StatusCode);
        Assert.Contains(OrderPayment.CashReceivedOnlyForCashMessage, await card.Content.ReadAsStringAsync());
    }
}
