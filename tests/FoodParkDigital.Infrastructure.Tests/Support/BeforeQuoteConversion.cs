using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 29E added (the service order an accepted quote became, when, and what it applied of the visit):
/// the app as deployed before it, and what a test needs to seed or read on a database migrated to an earlier migration, where
/// the columns do not exist yet.
/// </summary>
internal static class BeforeQuoteConversion
{
    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know them either (one before the quotes ignores them whole).</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        BeforeAppointmentWork.Ignore(modelBuilder);
        if (modelBuilder.Model.FindEntityType(typeof(Quote)) is null)
            return;

        // The relationship to the order first: left alone, it would keep OrderId as a shadow property.
        var quotes = modelBuilder.Entity<Quote>();
        foreach (var foreignKey in quotes.Metadata.GetForeignKeys()
                     .Where(k => k.Properties.Any(p => p.Name == nameof(Quote.OrderId))).ToList())
            quotes.Metadata.RemoveForeignKey(foreignKey);
        quotes.Ignore(q => q.OrderId);
        quotes.Ignore(q => q.ConvertedAt);
        quotes.Ignore(q => q.AppliedVisitCredit);
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
