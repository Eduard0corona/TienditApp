using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 24K added (what happens in Mercado Pago reaches the order): the app as deployed before it, and
/// what a test needs to seed or read online payments on a database migrated to an earlier migration, where the columns do not
/// exist yet.
/// </summary>
internal static class BeforeOnlinePaymentReconciliation
{
    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know it either.</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OnlineCheckout>().Ignore(c => c.Dispute);
        modelBuilder.Entity<OnlineCheckout>().Ignore(c => c.DisputeUpdatedAt);
        modelBuilder.Entity<OnlinePaymentAccount>().Ignore(a => a.PaymentsReviewedUntil);
        BeforeOrderInvoicing.Ignore(modelBuilder);
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
