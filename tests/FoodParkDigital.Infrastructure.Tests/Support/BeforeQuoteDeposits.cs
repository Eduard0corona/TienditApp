using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 31N added (the share of a deposit a business asks, the deposit a version asks and the quote a
/// checkout pays): the app as deployed before it, and what a test needs on a database migrated to an earlier migration,
/// where its columns do not exist yet.
/// </summary>
internal static class BeforeQuoteDeposits
{
    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know them either.</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantSettings>().Ignore(settings => settings.QuoteDepositPercent);
        modelBuilder.Entity<Quote>().Ignore(quote => quote.DepositAmount);
        // The quote a checkout pays: its key to the quotes and its index go with it.
        var checkout = modelBuilder.Entity<OnlineCheckout>();
        foreach (var foreignKey in checkout.Metadata.GetForeignKeys()
                     .Where(k => k.Properties.Any(p => p.Name == nameof(OnlineCheckout.QuoteId)))
                     .ToList())
            checkout.Metadata.RemoveForeignKey(foreignKey);
        foreach (var index in checkout.Metadata.GetIndexes()
                     .Where(i => i.Properties.Any(p => p.Name == nameof(OnlineCheckout.QuoteId)))
                     .ToList())
            checkout.Metadata.RemoveIndex(index);
        checkout.Ignore(c => c.QuoteId);
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
