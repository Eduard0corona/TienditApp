using FoodParkDigital.Application.Customers.Queries;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Domain.WhatsApp.Entities;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>The customer card and orders taken from a WhatsApp chat.</summary>
public class CustomerCardTests
{
    [Fact]
    public async Task Card_ShouldJoinTheCustomerAcrossPhoneFormsAndLeaveOutCancelledOrders()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var coffee = await fixture.AddProductAsync(tenant, "Café", 30m);
        var bread = await fixture.AddProductAsync(tenant, "Concha", 15m);
        await fixture.OpenCashSessionAsync(tenant);
        var handler = fixture.CreateOrderHandler();

        var paid = await handler.Handle(Sale("Ana López", "55 1234 5678", (coffee.Id, 2), (bread.Id, 1)) with
        {
            Payments = [new CreateOrderPaymentDto { Amount = 75m, Method = OrderPaymentMethod.Cash }]
        }, CancellationToken.None);
        var fromChat = await handler.Handle(Sale("Ana", "+5215512345678", (coffee.Id, 1)) with { Channel = OrderChannel.WhatsApp }, CancellationToken.None);
        var cancelled = await handler.Handle(Sale("Ana", "5512345678", (bread.Id, 5)), CancellationToken.None);
        Assert.True(paid.IsSuccess && fromChat.IsSuccess && cancelled.IsSuccess, paid.Error ?? fromChat.Error ?? cancelled.Error);
        var toCancel = await fixture.Context.Orders.SingleAsync(o => o.Id == cancelled.Value.Id);
        toCancel.Cancel(Domain.Orders.StateMachines.OrderStateMachineFactory.Create(BusinessType.Retail, OrderType.Retail), "test", "error");
        fixture.Context.MessagingOptOuts.Add(new MessagingOptOut(tenant.Id, "+5215512345678", OptOutSource.CustomerKeyword, SalesTestFixture.Now));
        var conversation = new WhatsAppConversation(tenant.Id, "+5215512345678", "Ana WhatsApp");
        fixture.Context.WhatsAppConversations.Add(conversation);
        await fixture.Context.SaveChangesAsync();

        var result = await new GetCustomerCardQueryHandler(fixture.Context, new PhoneNormalizationService(), fixture.TenantService, fixture.Clock)
            .Handle(new GetCustomerCardQuery { Phone = "+52 55 1234 5678" }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var card = result.Value;
        Assert.Equal("+525512345678", card.Phone);
        Assert.Equal(2, card.OrdersCount);
        Assert.Equal(75m, card.TotalSpent);
        Assert.Equal(30m, card.Balance);
        Assert.Equal(3, card.RecentOrders.Count);
        Assert.Contains(card.RecentOrders, o => o.Channel == OrderChannel.WhatsApp);
        Assert.Equal("Café", card.FavoriteProducts[0].ProductName);
        Assert.Equal(3, card.FavoriteProducts[0].Quantity);
        Assert.DoesNotContain(card.FavoriteProducts, p => p.Quantity == 6);
        Assert.True(card.OptedOut);
        Assert.Equal(conversation.Id, card.ConversationId);
    }

    [Fact]
    public void Channel_ShouldNotAcceptOnlineFromTheBusinessScreens()
    {
        var validator = new CreateOrderCommandValidator();

        Assert.True(validator.Validate(Sale("Ana", "5512345678", (Guid.NewGuid(), 1)) with { Channel = OrderChannel.WhatsApp }).IsValid);
        Assert.False(validator.Validate(Sale("Ana", "5512345678", (Guid.NewGuid(), 1)) with { Channel = OrderChannel.Online }).IsValid);
    }

    private static CreateOrderCommand Sale(string name, string phone, params (Guid ProductId, int Quantity)[] items) => new()
    {
        CustomerName = name,
        CustomerPhone = phone,
        CreatedByBusiness = true,
        OrderType = OrderType.Retail,
        Items = items.Select(i => new CreateOrderItemDto { ProductId = i.ProductId, Quantity = i.Quantity }).ToList()
    };
}
