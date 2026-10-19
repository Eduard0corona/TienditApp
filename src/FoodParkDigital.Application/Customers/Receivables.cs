using System.Linq.Expressions;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Customers;

/// <summary>
/// What customers owe the business (Fase 26A, ADR 0033), the one rule behind Clientes › Por cobrar, the account of a
/// customer, the limit of a sale on credit and «Debe» in the customer card. An order is owed when it was not cancelled,
/// carries the customer's phone, has a balance, and was sold on credit or already handed over (delivered or completed). An
/// order still in progress is not owed yet, and one the courier went out to collect in cash is what the courier owes
/// (Fase 22D), not the customer. Orders are grouped by the phone, however it was stored (+52 or +521).
/// </summary>
public static class Receivables
{
    public static readonly Expression<Func<Order, bool>> IsOwed = o =>
        o.Status != OrderStatus.Cancelled
        && o.CustomerPhone != null
        && o.Total > o.PaidAmount
        && (o.SoldOnCredit || o.Status == OrderStatus.Delivered || o.Status == OrderStatus.Completed)
        // Order.AwaitsCourierCash, written out so the database reads it.
        && !(o.Delivery != null && o.Delivery.CashToCollect != null && o.Status == OrderStatus.Delivered);

    /// <summary>The orders owed, each with the phone they are grouped by (<see cref="MessagingPhone.ToKey"/>).</summary>
    public static IQueryable<OwedOrder> Lines(IQueryable<Order> orders) => orders
        .Where(IsOwed)
        .Select(o => new OwedOrder
        {
            OrderId = o.Id,
            OrderNumber = o.OrderNumber,
            // MessagingPhone.ToKey, written out so the database groups by it.
            Phone = o.CustomerPhone!.Length == 14 && o.CustomerPhone.StartsWith("+521")
                ? "+52" + o.CustomerPhone.Substring(4, 10)
                : o.CustomerPhone,
            CustomerName = o.CustomerName,
            CreatedAt = o.CreatedAt,
            Status = o.Status,
            SoldOnCredit = o.SoldOnCredit,
            Total = o.Total,
            Paid = o.PaidAmount,
            Balance = o.Total - o.PaidAmount
        });

    /// <summary>The orders a customer owes, by the phone key.</summary>
    public static IQueryable<OwedOrder> OfCustomer(IQueryable<Order> orders, string phoneKey)
    {
        var variants = MessagingPhone.Variants(phoneKey).ToList();
        return Lines(orders.Where(o => o.CustomerPhone != null && variants.Contains(o.CustomerPhone)));
    }

    /// <summary>What a customer owes, by the phone key.</summary>
    public static async Task<decimal> OwedAsync(IQueryable<Order> orders, string phoneKey, CancellationToken cancellationToken)
        => await OfCustomer(orders, phoneKey).SumAsync(o => (decimal?)o.Balance, cancellationToken) ?? 0m;
}

/// <summary>An order a customer owes.</summary>
public sealed class OwedOrder
{
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = null!;
    /// <summary>The phone key the customer's orders are grouped by.</summary>
    public string Phone { get; init; } = null!;
    public string? CustomerName { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public OrderStatus Status { get; init; }
    public bool SoldOnCredit { get; init; }
    public decimal Total { get; init; }
    public decimal Paid { get; init; }
    public decimal Balance { get; init; }
}
