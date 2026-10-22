using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Customers;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests.Support;

/// <summary>
/// What the tests of the fiado's messages share (Fases 26C and 26E): a shop selling soap, its customers and their sales on
/// credit, WhatsApp connected, the settings screen's handlers, Por cobrar and the account, and a WhatsApp that only counts.
/// </summary>
internal static class CreditTestKit
{
    internal static async Task<(Tenant Tenant, Product Soap)> ShopAsync(SalesTestFixture fixture)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soap = await fixture.AddProductAsync(tenant, "Jabón", 100m);
        return (tenant, soap);
    }

    internal static async Task<Customer> AddCustomerAsync(
        SalesTestFixture fixture, Tenant tenant, string name, string phone, decimal? creditLimit = null, int? termDays = null)
    {
        var customer = new Customer(tenant.Id, name, phone, null);
        if (creditLimit is not null)
            customer.SetCreditLimit(creditLimit);
        customer.SetCreditTermDays(termDays);
        fixture.Context.Customers.Add(customer);
        await fixture.Context.SaveChangesAsync();
        return customer;
    }

    /// <summary>A sale of soap on credit, stamped when it was made (the in-memory context leaves the date to the test).</summary>
    internal static async Task<Order> SellOnCreditAsync(SalesTestFixture fixture, Customer customer, Product soap, decimal quantity, DateTimeOffset createdAt)
    {
        var sale = await fixture.CreateOrderHandler().Handle(new CreateOrderCommand
        {
            CustomerId = customer.Id,
            OnCredit = true,
            Items = [new CreateOrderItemDto { ProductId = soap.Id, Quantity = quantity }]
        }, default);
        Assert.True(sale.IsSuccess, sale.Error);
        var order = await fixture.Context.Orders.SingleAsync(o => o.Id == sale.Value.Id);
        order.CreatedAt = createdAt;
        await fixture.Context.SaveChangesAsync();
        return order;
    }

    internal static async Task SetBusinessTermAsync(SalesTestFixture fixture, int? days)
    {
        (await fixture.Context.TenantSettings.SingleAsync()).SetCreditTermDays(days);
        await fixture.Context.SaveChangesAsync();
    }

    internal static async Task ConnectWhatsAppAsync(SalesTestFixture fixture, Tenant tenant)
    {
        var config = new WhatsAppConfig(tenant.Id);
        config.Update(true, "token", "123456", "waba", "+52 55 0000 0000");
        fixture.Context.WhatsAppConfigs.Add(config);
        await fixture.Context.SaveChangesAsync();
    }


    internal static UpdateCreditRemindersCommandHandler UpdateRemindersHandler(SalesTestFixture fixture)
    {
        var context = fixture.Context;
        var mediator = new GetRemindersMediator(new GetCreditRemindersQueryHandler(context, fixture.TenantService));
        return new UpdateCreditRemindersCommandHandler(context, fixture.TenantService, fixture.CurrentUser, mediator);
    }

    internal static async Task<ReceivablesDto> ReceivablesAsync(SalesTestFixture fixture, GetReceivablesQuery query)
        => await new GetReceivablesQueryHandler(fixture.Context, new PhoneNormalizationService(), fixture.Clock).Handle(query, default);

    internal static async Task<CustomerAccountDto> AccountAsync(SalesTestFixture fixture, Guid customerId)
    {
        var result = await new GetCustomerAccountQueryHandler(fixture.Context, new PhoneNormalizationService(), fixture.Clock)
            .Handle(new GetCustomerAccountQuery { CustomerId = customerId }, default);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value;
    }

    /// <summary>Answers the settings query the update command sends back, and nothing else.</summary>
    internal sealed class GetRemindersMediator(GetCreditRemindersQueryHandler handler) : IMediator
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
            => request is GetCreditRemindersQuery query ? (TResponse)(object)await handler.Handle(query, ct) : default!;
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => Task.CompletedTask;
        public Task<object?> Send(object request, CancellationToken ct = default) => Task.FromResult<object?>(null);
        public Task Publish(object notification, CancellationToken ct = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => AsyncEnumerable.Empty<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => AsyncEnumerable.Empty<object?>();
    }

    internal sealed class FakeWhatsApp : IWhatsAppService
    {
        public List<(string Kind, string Phone)> Sent { get; } = [];

        public Task<MessageSendResult> SendTemplateMessageAsync(Guid tenantId, string phoneNumber, string templateName, string languageCode, string[] parameters, CancellationToken cancellationToken = default)
        {
            Sent.Add(($"template:{templateName}", phoneNumber));
            return Task.FromResult(MessageSendResult.Sent($"wamid.{Sent.Count}"));
        }

        public Task<MessageSendResult> SendTextMessageAsync(Guid tenantId, string phoneNumber, string message, CancellationToken cancellationToken = default)
        {
            Sent.Add(("text", phoneNumber));
            return Task.FromResult(MessageSendResult.Sent($"wamid.{Sent.Count}"));
        }
    }

    /// <summary>The in-memory context leaves the date to the test: stamps the queued messages as the database would, and saves.</summary>
    internal static async Task SaveQueuedAsync(SalesTestFixture fixture)
    {
        foreach (var entry in fixture.Context.ChangeTracker.Entries<OutgoingMessage>().Where(e => e.State == EntityState.Added))
            entry.Entity.CreatedAt = fixture.Clock.UtcNow;
        await fixture.Context.SaveChangesAsync();
    }
}
