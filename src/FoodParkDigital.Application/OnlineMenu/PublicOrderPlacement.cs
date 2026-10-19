using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Customers;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Delivery;
using FoodParkDigital.Application.Finance;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Tenants.Hours;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.OnlineMenu;

/// <summary>
/// What a customer of the online menu orders, with the phone already normalized: placed at once to pay at the business
/// (<see cref="PlacePublicOrderCommand"/>), or kept while it is paid online and placed then (Fase 24I, ADR 0031), which is
/// why it is stored as it is.
/// </summary>
public sealed record PublicOrderRequest
{
    public string CustomerName { get; init; } = null!;
    public string CustomerPhone { get; init; } = null!;
    public string? Notes { get; init; }
    public string? TableLabel { get; init; }
    public List<CreateOrderItemDto> Items { get; init; } = new();
    public OrderDeliveryInput? Delivery { get; init; }
    public bool SaveAddress { get; init; }
    public string? AddressLabel { get; init; }
}

/// <summary>
/// The rules of an order of the online menu, shared by the order paid at the business and the one paid online: whether the
/// business takes it now, the order itself with its prices, delivery and total (built without storing anything), and
/// placing it (folio, supplies, the customer's address and its tracking link).
/// </summary>
internal sealed class PublicOrderPlacement
{
    private readonly IApplicationDbContext _context;

    public PublicOrderPlacement(IApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// The branch that takes the order now, or why it is not taken. Closed, the business takes no pickup or delivery
    /// orders; a customer at one of its tables is already there, except on a closed special day. Online orders arrive at
    /// the default branch of the business, and only while a drawer is open there. The same rule as the menu and Explorar.
    /// A kitchen without premises that delivers has nowhere to pick an order up: it goes to an address, or to a table of the
    /// business in use (anyone can type a table into the link). Without delivery zones, orders arrive as before and the
    /// business arranges it (Fase 22C).
    /// </summary>
    public async Task<(Guid? BranchId, string? Refusal)> TakesOrderAsync(
        Tenant tenant, PublicOrderRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var hours = await BusinessHours.ReadAsync(
            _context, tenant.Id, tenant.Settings!.OpeningHours, tenant.Settings.TimeZone, now, cancellationToken);
        var seated = OnlineOrderAvailability.TableMayOrderWhileClosed(hours)
            && await PublicMenuTables.IsSeatedAsync(_context, tenant.Id, request.TableLabel, cancellationToken);
        var branchId = await BranchRules.DefaultBranchIdAsync(_context, tenant.Id, cancellationToken);
        var drawerOpen = await OrderCashService.HasOpenDrawerAtBranchAsync(_context, tenant.Id, branchId, cancellationToken);
        switch (OnlineOrderAvailability.Gate(tenant.Settings.OnlineMenuMode, hours, drawerOpen, seated))
        {
            case OnlineOrderGate.Closed:
                return (branchId, BusinessHoursText.ClosedMessage(hours!));
            case OnlineOrderGate.Paused:
                return (branchId, PlacePublicOrderCommandHandler.OrdersPausedMessage);
        }

        if (request.Delivery is null
            && await _context.Branches.AnyAsync(b => b.Id == branchId && b.ServiceMode == BranchServiceMode.DeliveryOnly, cancellationToken)
            && await DeliveryZoneLookup.AnyActiveAsync(_context, tenant.Id, cancellationToken)
            && !await PublicMenuTables.IsSeatedAsync(_context, tenant.Id, request.TableLabel, cancellationToken))
            return (branchId, PlacePublicOrderCommandHandler.DeliveryRequiredMessage);

        return (branchId, null);
    }

    /// <summary>
    /// Who orders a draft that is only priced and never stored (<see cref="BuildAsync"/>): the customer account, or a
    /// placeholder for the guest, so no customer record exists before the order does.
    /// </summary>
    public static CustomerLookupResult DraftCustomer(string phone, Guid? accountId, string name)
        => new(phone, accountId, accountId is null ? Guid.NewGuid() : null, name.Trim());

    /// <summary>
    /// The order, with today's prices and availability, the delivery zone's minimum and fee, and its total. Nothing is
    /// stored or tracked: a refused order leaves no trace.
    /// </summary>
    public async Task<(Order? Order, string? Refusal)> BuildAsync(
        Tenant tenant,
        PublicOrderRequest request,
        Guid? branchId,
        CustomerLookupResult customer,
        TenantSalesSettings settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var localTimeOfDay = TenantDateTimeResolver.LocalNow(now, settings.TimeZone).TimeOfDay;
        var order = new Order(
            tenantId: tenant.Id,
            customerUserId: customer.CustomerUserId,
            temporaryCustomerId: customer.TemporaryCustomerId,
            createdByBusiness: false,
            orderNumber: Order.NumberNotAssigned,
            businessType: tenant.BusinessType,
            orderType: OrderType.Retail,
            customerName: request.CustomerName.Trim(),
            customerPhone: customer.PhoneNormalized,
            customerNotes: string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            tableNumber: string.IsNullOrWhiteSpace(request.TableLabel) ? null : request.TableLabel.Trim(),
            pricesIncludeTax: settings.PricesIncludeTax,
            channel: OrderChannel.Online,
            branchId: branchId);

        var lineBuilder = new OrderLineBuilder(localTimeOfDay, settings.DefaultTaxRate);
        var products = await OrderCatalog.LoadProductsAsync(_context.Products, request.Items, cancellationToken);
        foreach (var item in request.Items)
        {
            var product = products[item.ProductId];
            if (!product.IsAvailable || !OrderProductClassifier.MatchesOrderType(OrderType.Retail, product.Unit))
                return (null, $"'{product.Name}' no se puede pedir en línea.");

            var built = lineBuilder.Build(product, item);
            if (built.IsFailure)
                return (null, built.Error!);
            order.AddItem(built.Value.Line);
        }

        // To the customer's address: the zone's minimum and fee, on the products, before the total is compared.
        if (request.Delivery is not null)
        {
            var deliveryError = await HomeDeliveryPlacement.ArrangeAsync(
                _context, order, request.Delivery, settings.DefaultTaxRate, cancellationToken);
            if (deliveryError is not null)
                return (null, deliveryError);
        }

        return (order, null);
    }

    /// <summary>
    /// Places a built order (the caller adds it and saves, in the transaction of its request): to preparation when the
    /// business accepts orders without confirming them, its folio, the supplies it uses, the address the customer account
    /// keeps and its tracking link.
    /// </summary>
    /// <returns>Whether it went straight to preparation.</returns>
    public async Task<bool> PlaceAsync(
        Order order,
        Tenant tenant,
        PublicOrderRequest request,
        Guid? accountId,
        DateTimeOffset now,
        IFolioGenerator folios,
        IOrderInventoryService orderInventoryService,
        CancellationToken cancellationToken)
    {
        // Businesses that accept orders automatically send them straight to preparation.
        var accepted = tenant.Settings!.AutoAcceptOrders;
        if (accepted)
        {
            var stateMachine = OrderStateMachineFactory.Create(order.BusinessType, order.OrderType);
            if (stateMachine.GetAllowedTransitions(order.Status).Contains(OrderStatus.Confirmed))
                order.AdvanceOnCreation(OrderStatus.Confirmed, stateMachine, "menu-en-linea");
            else
                accepted = false;
        }

        order.AssignNumber(await folios.NextOrderNumberAsync(tenant.Id, cancellationToken));

        // The customer's order is never refused for recipe stock: the business sees it and decides.
        await orderInventoryService.ApplyConsumptionForOrderAsync(order, cancellationToken, StockShortageHandling.Allow);
        if (accountId is { } account && request.Delivery is not null)
            await RememberAddressAsync(account, request, now, cancellationToken);
        order.IssueTrackingToken(OrderTracking.NewToken());
        return accepted;
    }

    /// <summary>
    /// The account offers this address first from now on: the one it keeps for the same place, or a new one when the
    /// customer asked to keep it and the account has room. A full account still orders: the address only stays out.
    /// </summary>
    private async Task RememberAddressAsync(Guid accountId, PublicOrderRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var delivery = request.Delivery!;
        var address = DeliveryAddress.From(delivery.PostalCode, delivery.AddressLine, delivery.References);
        var kept = await AccountAddresses.OfAsync(_context, accountId, cancellationToken, tracking: true);
        var saved = kept.FirstOrDefault(existing => existing.IsSamePlaceAs(address));
        if (saved is null && request.SaveAddress && kept.Count < AccountAddress.MaxPerAccount)
        {
            saved = AccountAddress.Create(accountId, request.AddressLabel, address.PostalCode, address.AddressLine, address.References, kept.Count);
            _context.AccountAddresses.Add(saved);
        }

        saved?.MarkUsed(address, now);
    }
}
