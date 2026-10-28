using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 28I added (the credit note of a sale of a global invoice): the app as deployed before it, and
/// what a test needs to seed or read credit notes on a database migrated to an earlier migration, where the column does not
/// exist yet.
/// </summary>
internal static class BeforeGlobalInvoiceCreditNotes
{
    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know it either.</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        var note = modelBuilder.Entity<CreditNote>();
        foreach (var foreignKey in note.Metadata.GetForeignKeys()
                     .Where(k => k.Properties.Any(p => p.Name == nameof(CreditNote.GlobalInvoiceId)))
                     .ToList())
            note.Metadata.RemoveForeignKey(foreignKey);
        foreach (var index in note.Metadata.GetIndexes()
                     .Where(i => i.Properties.Any(p => p.Name == nameof(CreditNote.GlobalInvoiceId)))
                     .ToList())
            note.Metadata.RemoveIndex(index);
        note.Ignore(n => n.GlobalInvoiceId);
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
