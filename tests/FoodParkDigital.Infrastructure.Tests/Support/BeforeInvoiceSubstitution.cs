using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 28G added (the invoice that replaces another): the app as deployed before it, and what a
/// test needs to seed or read invoices on a database migrated to an earlier migration, where the column does not exist yet.
/// </summary>
internal static class BeforeInvoiceSubstitution
{
    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know it either.</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        // Before the 28G is also before the 28I.
        BeforeGlobalInvoiceCreditNotes.Ignore(modelBuilder);
        var invoice = modelBuilder.Entity<OrderInvoice>();
        foreach (var index in invoice.Metadata.GetIndexes()
                     .Where(i => i.Properties.Any(p => p.Name == nameof(OrderInvoice.ReplacedByInvoiceId)))
                     .ToList())
            invoice.Metadata.RemoveIndex(index);
        invoice.Ignore(i => i.ReplacedByInvoiceId);
        // One live invoice per order, as it was.
        invoice.HasIndex(i => new { i.TenantId, i.OrderId }, "IX_OrderInvoices_TenantId_OrderId_Live")
            .IsUnique()
            .HasFilter("[Status] IN (0, 1, 3)");
    }

    private sealed class Context(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentTenantService tenantService,
        IMediator mediator) : ApplicationDbContext(options, tenantService, mediator)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            Ignore(modelBuilder);
        }
    }
}
