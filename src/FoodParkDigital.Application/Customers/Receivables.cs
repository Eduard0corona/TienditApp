using System.Linq.Expressions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Utils;
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
        // Order.Balance above zero: what the business forgave is not owed (Fase 26B).
        && o.Total > o.PaidAmount + o.WrittenOffAmount
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
            WrittenOff = o.WrittenOffAmount,
            Balance = o.Total - o.PaidAmount - o.WrittenOffAmount
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
    /// <summary>What the business forgave of it (Fase 26B).</summary>
    public decimal WrittenOff { get; init; }
    public decimal Balance { get; init; }
}

/// <summary>
/// The term of the fiado (Fase 26B, ADR 0034): an order owed is overdue once its last day to pay, the business day it was
/// made plus the term, has passed in the business's time zone. Without a term nothing is overdue.
/// </summary>
public static class CreditTerm
{
    /// <summary>The business's term and time zone, as its settings keep them.</summary>
    public sealed record Settings(int? Days, string? TimeZone)
    {
        /// <summary>
        /// The orders made before this instant are overdue today: the start of the business day <see cref="Days"/> days
        /// ago. Without a term, the earliest instant, so nothing is.
        /// </summary>
        public DateTimeOffset OverdueBefore(DateTimeOffset utcNow)
        {
            if (Days is not { } days)
                return DateTimeOffset.MinValue;
            var zone = TenantDateTimeResolver.Resolve(TimeZone);
            var today = TimeZoneInfo.ConvertTime(utcNow, zone).Date;
            return TenantDateTimeResolver.LocalDateBoundaryToUtc(today.AddDays(-days), startOfDay: true, zone);
        }

        /// <summary>The last day to pay an order made at <paramref name="createdAt"/>, in the business's calendar.</summary>
        public DateOnly? DueOn(DateTimeOffset createdAt) => Days is { } days
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(createdAt, TenantDateTimeResolver.Resolve(TimeZone)).Date.AddDays(days))
            : null;
    }

    /// <summary>The settings of the business of the request (the tenant filter picks them).</summary>
    public static async Task<Settings> ReadAsync(IApplicationDbContext context, CancellationToken cancellationToken)
        => await context.TenantSettings.AsNoTracking()
            .Select(s => new Settings(s.CreditTermDays, s.TimeZone))
            .FirstOrDefaultAsync(cancellationToken)
            ?? new Settings(null, null);
}
