using System.Linq.Expressions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Domain.Customers.Entities;
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
            // Owed since it was sold on credit, else since it was first handed over: a service received days before
            // it is picked up unpaid starts its term that day. Before the status history, since it was made.
            OwedSince = o.SoldOnCredit
                ? o.CreatedAt
                : o.StatusHistory
                    .Where(h => h.NewStatus == OrderStatus.Delivered || h.NewStatus == OrderStatus.Completed)
                    .Min(h => (DateTimeOffset?)h.ChangedAt) ?? o.CreatedAt,
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
    /// <summary>
    /// When the customer started to owe it, which the term counts from (Fase 26B): when it was sold on credit (made), else
    /// when it was first delivered or completed.
    /// </summary>
    public DateTimeOffset OwedSince { get; init; }
    public OrderStatus Status { get; init; }
    public bool SoldOnCredit { get; init; }
    public decimal Total { get; init; }
    public decimal Paid { get; init; }
    /// <summary>What the business forgave of it (Fase 26B).</summary>
    public decimal WrittenOff { get; init; }
    public decimal Balance { get; init; }
}

/// <summary>
/// The term of the fiado (Fase 26B, ADR 0034): an order owed is overdue once its last day to pay, the business day the
/// customer started to owe it (<see cref="OwedOrder.OwedSince"/>) plus the term, has passed in the business's time zone.
/// A customer may have a term of their own instead of the business's (Fase 26C, ADR 0035); without either, nothing is
/// overdue.
/// </summary>
public static class CreditTerm
{
    /// <summary>The business's term and time zone, as its settings keep them.</summary>
    public sealed record Settings(int? Days, string? TimeZone)
    {
        /// <summary>The same settings with a customer's own term, when they have one.</summary>
        public Settings For(int? ownDays) => ownDays is null ? this : this with { Days = ownDays };

        /// <summary>
        /// The orders owed since before this instant are overdue today: the start of the business day <see cref="Days"/>
        /// days ago. Without a term, the earliest instant, so nothing is.
        /// </summary>
        public DateTimeOffset OverdueBefore(DateTimeOffset utcNow) => OwedBefore(utcNow, 0);

        /// <summary>
        /// The orders owed since before this instant come due by the end of the business day <paramref name="daysAhead"/>
        /// days from today (Fase 26E), or are overdue already: the start of the business day <see cref="Days"/> minus
        /// <paramref name="daysAhead"/> plus one days ago. Without a term, the earliest instant, so nothing does.
        /// </summary>
        public DateTimeOffset DueByBefore(DateTimeOffset utcNow, int daysAhead) => OwedBefore(utcNow, daysAhead + 1);

        /// <summary>The start of the business day <see cref="Days"/> days before today, moved <paramref name="offset"/> days on.</summary>
        private DateTimeOffset OwedBefore(DateTimeOffset utcNow, int offset)
        {
            if (Days is not { } days)
                return DateTimeOffset.MinValue;
            var zone = TenantDateTimeResolver.Resolve(TimeZone);
            var today = TimeZoneInfo.ConvertTime(utcNow, zone).Date;
            // Not LocalDateBoundaryToUtc: a day without 00:00 would throw inside the payment or write-off that reads it.
            return TenantDateTimeResolver.StartOfLocalDayUtc(today.AddDays(offset - days), zone);
        }

        /// <summary>
        /// When the orders whose last day to pay is <paramref name="dueOn"/> started to be owed (Fase 26E): from the start of
        /// the business day <see cref="Days"/> days before it until the start of the next one. Null without a term.
        /// </summary>
        public (DateTimeOffset From, DateTimeOffset Until)? OwedSinceFor(DateOnly dueOn)
        {
            if (Days is not { } days)
                return null;
            var zone = TenantDateTimeResolver.Resolve(TimeZone);
            var owedOn = dueOn.ToDateTime(TimeOnly.MinValue).AddDays(-days);
            return (TenantDateTimeResolver.StartOfLocalDayUtc(owedOn, zone), TenantDateTimeResolver.StartOfLocalDayUtc(owedOn.AddDays(1), zone));
        }

        /// <summary>The last day to pay an order owed since <paramref name="owedSince"/>, in the business's calendar.</summary>
        public DateOnly? DueOn(DateTimeOffset owedSince) => Days is { } days
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(owedSince, TenantDateTimeResolver.Resolve(TimeZone)).Date.AddDays(days))
            : null;
    }

    /// <summary>The settings of the business of the request (the tenant filter picks them).</summary>
    public static async Task<Settings> ReadAsync(IApplicationDbContext context, CancellationToken cancellationToken)
        => await context.TenantSettings.AsNoTracking()
            .Select(s => new Settings(s.CreditTermDays, s.TimeZone))
            .FirstOrDefaultAsync(cancellationToken)
            ?? new Settings(null, null);

    /// <summary>
    /// The term of the customer with this phone key (Fase 26C): their customer record's own, or the shortest when two records
    /// share the phone; null when the business's applies (or there is no record).
    /// </summary>
    public static Task<int?> OwnDaysAsync(IApplicationDbContext context, string phoneKey, CancellationToken cancellationToken)
    {
        var forms = CustomerPhones.StoredForms(phoneKey);
        return context.Customers.AsNoTracking()
            .Where(c => !c.IsPublicGeneral && c.Phone != null && forms.Contains(c.Phone))
            .MinAsync(c => c.CreditTermDays, cancellationToken);
    }

    /// <summary>Whether the customer with this phone key owes something past their term now (Fase 26C).</summary>
    public static async Task<bool> HasOverdueAsync(
        IApplicationDbContext context, string phoneKey, Settings business, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        var before = business.For(await OwnDaysAsync(context, phoneKey, cancellationToken)).OverdueBefore(utcNow);
        return await Receivables.OfCustomer(context.Orders.AsNoTracking(), phoneKey).AnyAsync(l => l.OwedSince < before, cancellationToken);
    }

    /// <summary>
    /// Whether the customer with this phone key still owes something whose last day to pay, by their term now, is
    /// <paramref name="dueOn"/> (Fase 26E).
    /// </summary>
    public static async Task<bool> OwesDueOnAsync(
        IApplicationDbContext context, string phoneKey, Settings business, DateOnly dueOn, CancellationToken cancellationToken)
    {
        if (business.For(await OwnDaysAsync(context, phoneKey, cancellationToken)).OwedSinceFor(dueOn) is not { } owed)
            return false;
        var (from, until) = owed;
        return await Receivables.OfCustomer(context.Orders.AsNoTracking(), phoneKey)
            .AnyAsync(l => l.OwedSince >= from && l.OwedSince < until, cancellationToken);
    }

    /// <summary>
    /// The customers' own terms by the phone key their orders are grouped by (<see cref="Receivables.Lines"/>): a record saved
    /// before phones were normalized (ten digits) counts as +52, and the shortest wins when two records share the phone.
    /// </summary>
    public static IQueryable<PhoneTerm> OwnTerms(IQueryable<Customer> customers) => customers
        .Where(c => !c.IsPublicGeneral && c.Phone != null && c.CreditTermDays != null)
        // CustomerPhones.StoredForms read backwards, written out so the database groups by it.
        .Select(c => new
        {
            Phone = c.Phone!.Length == 10
                ? "+52" + c.Phone
                : c.Phone.Length == 14 && c.Phone.StartsWith("+521") ? "+52" + c.Phone.Substring(4, 10) : c.Phone,
            Days = c.CreditTermDays!.Value
        })
        .GroupBy(c => c.Phone)
        .Select(g => new PhoneTerm { Phone = g.Key, Days = g.Min(c => c.Days) });

    /// <summary>
    /// The orders of <paramref name="lines"/> that are overdue now, each by the term of its customer: their own, else the
    /// business's. Read by the database: each term in use is one condition with its own instant.
    /// </summary>
    public static Task<IQueryable<OwedOrder>> OverdueAsync(
        IApplicationDbContext context,
        IQueryable<OwedOrder> lines,
        Settings business,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
        => OwedBeforeAsync(context, lines, business, term => term.OverdueBefore(utcNow), cancellationToken);

    /// <summary>
    /// The orders of <paramref name="lines"/> whose last day to pay, each by the term of its customer, is at most
    /// <paramref name="daysAhead"/> days from today (Fase 26E): what comes due by then and what is overdue already.
    /// </summary>
    public static Task<IQueryable<OwedOrder>> DueByAsync(
        IApplicationDbContext context,
        IQueryable<OwedOrder> lines,
        Settings business,
        DateTimeOffset utcNow,
        int daysAhead,
        CancellationToken cancellationToken)
        => OwedBeforeAsync(context, lines, business, term => term.DueByBefore(utcNow, daysAhead), cancellationToken);

    private static async Task<IQueryable<OwedOrder>> OwedBeforeAsync(
        IApplicationDbContext context,
        IQueryable<OwedOrder> lines,
        Settings business,
        Func<Settings, DateTimeOffset> before,
        CancellationToken cancellationToken)
    {
        var ownTerms = OwnTerms(context.Customers.AsNoTracking());
        var ownDays = await ownTerms.Select(t => t.Days).Distinct().ToListAsync(cancellationToken);
        var termed =
            from line in lines
            join term in ownTerms on line.Phone equals term.Phone into terms
            from term in terms.DefaultIfEmpty()
            select new TermedOrder { Line = line, OwnDays = (int?)term.Days };
        return termed.Where(IsOwedBefore(business, ownDays, before)).Select(t => t.Line);
    }

    /// <summary>
    /// Owed since before the instant of its customer's own term when they have one, else of the business's: one branch per
    /// term in use, each with the start of the business day it reaches back to.
    /// </summary>
    private static Expression<Func<TermedOrder, bool>> IsOwedBefore(Settings business, IEnumerable<int> ownDays, Func<Settings, DateTimeOffset> before)
    {
        var order = Expression.Parameter(typeof(TermedOrder), "order");
        var own = Expression.Property(order, nameof(TermedOrder.OwnDays));
        var owedSince = Expression.Property(Expression.Property(order, nameof(TermedOrder.Line)), nameof(OwedOrder.OwedSince));
        // Through a holder, as a captured variable: the database gets each instant as a parameter, and the query stays
        // the same from one day (and business) to the next instead of a new one to compile.
        Expression OwedBefore(DateTimeOffset instant) =>
            Expression.LessThan(owedSince, Expression.Property(Expression.Constant(new Instant(instant)), nameof(Instant.Value)));

        Expression? owed = null;
        if (business.Days is not null)
            owed = Expression.AndAlso(Expression.Equal(own, Expression.Constant(null, typeof(int?))), OwedBefore(before(business)));
        foreach (var days in ownDays.Distinct().Order())
        {
            var branch = Expression.AndAlso(
                Expression.Equal(own, Expression.Constant(days, typeof(int?))),
                OwedBefore(before(business.For(days))));
            owed = owed is null ? branch : Expression.OrElse(owed, branch);
        }

        return Expression.Lambda<Func<TermedOrder, bool>>(owed ?? Expression.Constant(false), order);
    }

    private sealed class Instant(DateTimeOffset value)
    {
        public DateTimeOffset Value { get; } = value;
    }
}

/// <summary>A customer's own term, by the phone key their orders are grouped by.</summary>
public sealed class PhoneTerm
{
    public string Phone { get; init; } = null!;
    public int Days { get; init; }
}

/// <summary>An order owed with its customer's own term, if they have one.</summary>
public sealed class TermedOrder
{
    public OwedOrder Line { get; init; } = null!;
    public int? OwnDays { get; init; }
}
