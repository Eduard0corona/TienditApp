using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The refunds (Fase 28I, ADR 0054) and write-offs (Fase 28J, ADR 0055) of sales a global invoice covers that owe a credit
/// note, as the worker finds them on SQL Server: across businesses (without the business filter, each set joined by its
/// business) and within one. The query runs as SQL Server translates it, which the in-memory flow tests do not show.
/// </summary>
public class GlobalInvoiceCreditNotesSqlServerTests
{
    private static readonly DateTimeOffset Sold = new(2026, 10, 4, 17, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Stamped = new(2026, 10, 5, 15, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task PendingRefunds_ShouldFindTheRefundsOfSalesOfAStampedGlobalInvoice_WithoutACreditNoteOfIt()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = Guid.NewGuid();

        // A business whose stamped global invoice covers four sales.
        var tenant = NewTenant();
        var owing = PaidOrder(tenant.Id, "ORD-000001");
        var refused = PaidOrder(tenant.Id, "ORD-000002");
        var other = PaidOrder(tenant.Id, "ORD-000003");
        var voided = PaidOrder(tenant.Id, "ORD-000004");
        var global = Global(tenant.Id, 1, userId, owing, refused, other, voided);
        global.Stamp(Guid.NewGuid().ToString().ToUpperInvariant(), "sw", Stamped, "cfdi/g1.xml");
        // Another business: its global invoice still stamping, one cancelled (its sale is free again), and a stamped one
        // whose sale owes a credit note.
        var otherTenant = NewTenant();
        var waiting = PaidOrder(otherTenant.Id, "ORD-000001");
        var released = PaidOrder(otherTenant.Id, "ORD-000002");
        var theirs = PaidOrder(otherTenant.Id, "ORD-000003");
        var stamping = Global(otherTenant.Id, 1, userId, waiting);
        var cancelled = Global(otherTenant.Id, 2, userId, released);
        cancelled.Stamp(Guid.NewGuid().ToString().ToUpperInvariant(), "sw", Stamped, "cfdi/g2.xml");
        cancelled.RequestCancellation(CfdiCancellationMotive.ErrorsWithoutReplacement, userId, Stamped.AddHours(1));
        cancelled.Cancel(Stamped.AddHours(1));
        var theirGlobal = Global(otherTenant.Id, 3, userId, theirs);
        theirGlobal.Stamp(Guid.NewGuid().ToString().ToUpperInvariant(), "sw", Stamped, "cfdi/g3.xml");

        var at = Stamped.AddHours(2);
        var owingRefund = owing.RegisterRefund(20m, OrderPaymentMethod.Card, "Devolución", at, "cajero", null, null, false, []);
        var refusedRefund = refused.RegisterRefund(20m, OrderPaymentMethod.Card, "Devolución", at.AddMinutes(1), "cajero", null, null, false, []);
        other.RegisterRefund(20m, OrderPaymentMethod.Other, "Devolución", at.AddMinutes(2), "cajero", null, null, false, []);
        var voidedRefund = voided.RegisterRefund(100m, OrderPaymentMethod.Cash, "Anulada", at.AddMinutes(3), "cajero", null, null, false, []);
        waiting.RegisterRefund(20m, OrderPaymentMethod.Card, "Devolución", at, "cajero", null, null, false, []);
        released.RegisterRefund(20m, OrderPaymentMethod.Card, "Devolución", at, "cajero", null, null, false, []);
        var theirRefund = theirs.RegisterRefund(20m, OrderPaymentMethod.Card, "Devolución", at.AddMinutes(4), "cajero", null, null, false, []);
        // The credit note of `refused` was refused: it is issued again only by a person.
        var refusedNote = CreditNote.ForRefundInGlobalInvoice(
            global, global.Orders.Single(o => o.OrderId == refused.Id), refusedRefund, "NC", 1, 17.24m, 2.76m, "{}", null, at.AddMinutes(5));
        refusedNote.Refuse("CFDI40108", at.AddMinutes(6));

        await using (var context = database.CreateContext(tenant.Id))
        {
            context.AddRange(
                tenant, new TenantSettings(tenant.Id, TenantCapability.Retail),
                otherTenant, new TenantSettings(otherTenant.Id, TenantCapability.Retail),
                owing, refused, other, voided, waiting, released, theirs, global, stamping, cancelled, theirGlobal, refusedNote);
            // «Anular venta» leaves it cancelled once everything was given back.
            context.Entry(voided).Property(o => o.Status).CurrentValue = OrderStatus.Cancelled;
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext(tenant.Id))
        {
            // As the worker reads it: every business, each set joined by its business.
            var everyone = await CreditNotes.PendingRefunds(
                    context.OrderRefunds.IgnoreQueryFilters(),
                    context.Orders.IgnoreQueryFilters(),
                    context.OrderInvoices.IgnoreQueryFilters(),
                    context.GlobalInvoiceOrders.IgnoreQueryFilters(),
                    context.GlobalInvoices.IgnoreQueryFilters(),
                    context.CreditNotes.IgnoreQueryFilters())
                .OrderBy(r => r.RefundedAt)
                .Select(r => r.Id)
                .ToListAsync();
            Assert.Equal([owingRefund.Id, voidedRefund.Id, theirRefund.Id], everyone);

            // As a business's pass reads it: never the other business's.
            var own = await CreditNotes.PendingRefunds(
                    context.OrderRefunds, context.Orders, context.OrderInvoices, context.GlobalInvoiceOrders, context.GlobalInvoices,
                    context.CreditNotes)
                .OrderBy(r => r.RefundedAt)
                .Select(r => r.Id)
                .ToListAsync();
            Assert.Equal([owingRefund.Id, voidedRefund.Id], own);
        }
    }

    [SqlServerFact]
    public async Task PendingWriteOffs_ShouldFindTheWriteOffsOfSalesOfAStampedGlobalInvoice_WithoutACreditNoteOfIt()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var userId = Guid.NewGuid();
        var at = Sold.AddHours(1);

        // A business whose stamped global invoice covers three sales settled with a write-off: one owes its credit note, one
        // has it, and one was undone.
        var tenant = NewTenant();
        var owing = OwedOrder(tenant.Id, "ORD-000001");
        var noted = OwedOrder(tenant.Id, "ORD-000002");
        var undone = OwedOrder(tenant.Id, "ORD-000003");
        // Another business: its global invoice still stamping, one cancelled (its sale is free again), and a stamped one
        // whose sale owes a credit note.
        var otherTenant = NewTenant();
        var waiting = OwedOrder(otherTenant.Id, "ORD-000001");
        var released = OwedOrder(otherTenant.Id, "ORD-000002");
        var theirs = OwedOrder(otherTenant.Id, "ORD-000003");
        var owingWriteOff = owing.WriteOff(30m, OrderWriteOffReason.WontBeCollected, null, at, "dueña");
        var notedWriteOff = noted.WriteOff(30m, OrderWriteOffReason.Discount, null, at.AddMinutes(1), "dueña");
        var undoneWriteOff = undone.WriteOff(30m, OrderWriteOffReason.Courtesy, null, at.AddMinutes(2), "dueña");
        waiting.WriteOff(30m, OrderWriteOffReason.WontBeCollected, null, at, "dueña");
        released.WriteOff(30m, OrderWriteOffReason.WontBeCollected, null, at, "dueña");
        var theirWriteOff = theirs.WriteOff(30m, OrderWriteOffReason.Discount, null, at.AddMinutes(3), "dueña");

        var global = Global(tenant.Id, 1, userId, owing, noted, undone);
        global.Stamp(Guid.NewGuid().ToString().ToUpperInvariant(), "sw", Stamped, "cfdi/g1.xml");
        var stamping = Global(otherTenant.Id, 1, userId, waiting);
        var cancelled = Global(otherTenant.Id, 2, userId, released);
        cancelled.Stamp(Guid.NewGuid().ToString().ToUpperInvariant(), "sw", Stamped, "cfdi/g2.xml");
        cancelled.RequestCancellation(CfdiCancellationMotive.ErrorsWithoutReplacement, userId, Stamped.AddHours(1));
        cancelled.Cancel(Stamped.AddHours(1));
        var theirGlobal = Global(otherTenant.Id, 3, userId, theirs);
        theirGlobal.Stamp(Guid.NewGuid().ToString().ToUpperInvariant(), "sw", Stamped, "cfdi/g3.xml");
        // `noted` has its credit note (refused: issued again only by a person); `undone` was undone before it had one.
        var notedNote = CreditNote.ForWriteOffInGlobalInvoice(
            global, global.Orders.Single(o => o.OrderId == noted.Id), notedWriteOff, "NC", 1, 25.86m, 4.14m, "{}", null, Stamped.AddMinutes(5));
        notedNote.Refuse("CFDI40108", Stamped.AddMinutes(6));
        undone.ReverseWriteOff(undoneWriteOff.Id, Stamped.AddMinutes(7), "dueña");

        await using (var context = database.CreateContext(tenant.Id))
        {
            context.AddRange(
                tenant, new TenantSettings(tenant.Id, TenantCapability.Retail),
                otherTenant, new TenantSettings(otherTenant.Id, TenantCapability.Retail),
                owing, noted, undone, waiting, released, theirs, global, stamping, cancelled, theirGlobal, notedNote);
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext(tenant.Id))
        {
            // As the worker reads it: every business, each set joined by its business.
            var everyone = await CreditNotes.PendingWriteOffs(
                    context.OrderWriteOffs.IgnoreQueryFilters(),
                    context.OrderInvoices.IgnoreQueryFilters(),
                    context.GlobalInvoiceOrders.IgnoreQueryFilters(),
                    context.GlobalInvoices.IgnoreQueryFilters(),
                    context.CreditNotes.IgnoreQueryFilters())
                .OrderBy(w => w.WrittenOffAt)
                .Select(w => w.Id)
                .ToListAsync();
            Assert.Equal([owingWriteOff.Id, theirWriteOff.Id], everyone);

            // As a business's pass reads it: never the other business's.
            var own = await CreditNotes.PendingWriteOffs(
                    context.OrderWriteOffs, context.OrderInvoices, context.GlobalInvoiceOrders, context.GlobalInvoices, context.CreditNotes)
                .Select(w => w.Id)
                .ToListAsync();
            Assert.Equal([owingWriteOff.Id], own);
        }
    }

    private static Tenant NewTenant()
    {
        var slug = $"tienda-{Guid.NewGuid():N}"[..20];
        return new Tenant("Tienda Luna", slug, BusinessType.Retail, $"{slug}@demo.com");
    }

    /// <summary>A $100 sale paid in cash.</summary>
    private static Order PaidOrder(Guid tenantId, string number)
    {
        var order = new Order(tenantId, null, null, true, number, BusinessType.Retail, OrderType.Retail, "Público", null,
            null, null, pricesIncludeTax: true);
        order.AddItem(Guid.NewGuid(), "Café", 1, 100m);
        order.RegisterPayment(100m, OrderPaymentMethod.Cash, Sold, null, null, null, null);
        return order;
    }

    /// <summary>A $100 sale handed over with $70 paid in cash: the customer owes the rest.</summary>
    private static Order OwedOrder(Guid tenantId, string number)
    {
        var order = new Order(tenantId, null, null, true, number, BusinessType.Retail, OrderType.Retail, "Karla", "+525512345678",
            null, null, pricesIncludeTax: true);
        order.AddItem(Guid.NewGuid(), "Café", 1, 100m);
        order.RegisterPayment(70m, OrderPaymentMethod.Cash, Sold, null, null, null, null);
        order.TransitionTo(OrderStatus.Delivered, OrderStateMachineFactory.Create(BusinessType.Retail, OrderType.Retail), "cajero");
        return order;
    }

    private static GlobalInvoice Global(Guid tenantId, long folio, Guid userId, params Order[] sales)
        => GlobalInvoice.Request(
            tenantId, GlobalInvoicePeriod.Containing(GlobalInvoicePeriodicity.Daily, new DateOnly(2026, 10, 4)), "F", folio, "01", "{}",
            86.21m * sales.Length, 13.79m * sales.Length, 100m * sales.Length,
            sales.Select(sale => new GlobalInvoiceSale(sale.Id, sale.OrderNumber, 100m)).ToList(), userId, Stamped.AddMinutes(-1));
}
