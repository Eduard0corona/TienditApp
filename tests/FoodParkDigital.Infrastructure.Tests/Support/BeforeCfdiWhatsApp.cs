using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 28M added (the CFDI by WhatsApp): the app as deployed before it, and what a test needs to
/// seed or read invoices, invoicing profiles and queued messages on a database migrated to an earlier migration, where the
/// columns do not exist yet.
/// </summary>
internal static class BeforeCfdiWhatsApp
{
    private static readonly string[] MessageColumns =
    [
        nameof(OutgoingMessage.CfdiDocument), nameof(OutgoingMessage.CfdiId), nameof(OutgoingMessage.OrderInvoiceId),
        nameof(OutgoingMessage.RequestedByUserId), nameof(OutgoingMessage.LinkToken)
    ];

    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know it either.</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        IgnoreMessages(modelBuilder);
        modelBuilder.Entity<OrderInvoice>().Ignore(i => i.ReceiverWhatsApp);
        modelBuilder.Entity<InvoicingProfile>().Ignore(p => p.WhatsAppTemplateId);
    }

    /// <summary>Only the queue's columns, for a version that does not know the invoices at all.</summary>
    public static void IgnoreMessages(ModelBuilder modelBuilder)
    {
        var message = modelBuilder.Entity<OutgoingMessage>();
        foreach (var foreignKey in message.Metadata.GetForeignKeys()
                     .Where(k => k.Properties.Any(p => MessageColumns.Contains(p.Name)))
                     .ToList())
            message.Metadata.RemoveForeignKey(foreignKey);
        foreach (var index in message.Metadata.GetIndexes()
                     .Where(i => i.Properties.Any(p => MessageColumns.Contains(p.Name)))
                     .ToList())
            message.Metadata.RemoveIndex(index);
        foreach (var column in MessageColumns)
            message.Ignore(column);
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
