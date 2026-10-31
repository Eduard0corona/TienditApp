using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 28A added (the invoice of an order stamped with a PAC): the app as deployed before it, and
/// what a test needs to seed or read customers on a database migrated to an earlier migration, where the columns and the
/// tables do not exist yet.
/// </summary>
internal static class BeforeOrderInvoicing
{
    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know it either.</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().Ignore(c => c.FiscalData);
        modelBuilder.Entity<Customer>().Ignore(c => c.InvoiceEmail);
        // What came later (Fases 28C to 28E, the queue's CFDI of 28M) points to the invoices: it goes first, with the
        // emails of the month to the accountant (Fase 28O).
        BeforeCfdiWhatsApp.IgnoreMessages(modelBuilder);
        modelBuilder.Ignore<CfdiEmail>();
        modelBuilder.Ignore<CfdiMonthEmail>();
        modelBuilder.Ignore<CreditNote>();
        modelBuilder.Ignore<PaymentComplement>();
        modelBuilder.Ignore<OrderInvoice>();
        modelBuilder.Ignore<InvoicingProfile>();
        // What came after the invoices and does not point to them (the visits to quote of Fase 29A).
        BeforeHomeVisitRequests.Ignore(modelBuilder);
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
