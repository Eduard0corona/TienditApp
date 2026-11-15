using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// The orders AI assistants prepare through the MCP server (Fase 32B, ADR 0096). The lines are one JSON column, as a cart of
/// an account: read and written whole. The database keeps what each status means: a pending one has its lines and no order,
/// a confirmed one has its order and forgot its lines. Its link opens one order of the whole platform, as a tracking link.
/// </summary>
public class AssistantOrderConfiguration : IEntityTypeConfiguration<AssistantOrder>
{
    public void Configure(EntityTypeBuilder<AssistantOrder> builder)
    {
        builder.ToTable("AssistantOrders", table =>
        {
            table.HasCheckConstraint("CK_AssistantOrders_Token", $"LEN([Token]) = {Order.TrackingTokenLength}");
            table.HasCheckConstraint("CK_AssistantOrders_ItemsTotal", "[ItemsTotal] >= 0");
            table.HasCheckConstraint("CK_AssistantOrders_Status", "[Status] IN (0, 1)");
            table.HasCheckConstraint(
                "CK_AssistantOrders_Outcome",
                "([Status] = 0 AND [OrderId] IS NULL AND [ConfirmedAt] IS NULL)"
                + " OR ([Status] = 1 AND [OrderId] IS NOT NULL AND [ConfirmedAt] IS NOT NULL)");
            table.HasCheckConstraint("CK_AssistantOrders_PostalCode", "[PostalCode] IS NULL OR LEN([PostalCode]) = 5");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Token).HasMaxLength(Order.TrackingTokenLength).IsUnicode(false).IsRequired();
        builder.Property<List<AccountCartLine>>("_lines")
            .HasColumnName("Lines")
            .HasConversion(AccountCartLinesStorage.Converter, AccountCartLinesStorage.Comparer)
            .IsUnicode()
            .IsRequired();
        builder.Ignore(x => x.Lines);
        builder.Property(x => x.PostalCode).HasMaxLength(5).IsUnicode(false);
        builder.Property(x => x.ItemsTotal).HasPrecision(18, 2);
        builder.Property(x => x.Source).HasMaxLength(AssistantOrder.MaxSourceLength);
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.PreparedAt).IsRequired();
        builder.Property(x => x.ExpiresAt).IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.NoAction);

        // Its link opens it from anywhere: whoever follows it has no business yet.
        builder.HasIndex(x => x.Token).IsUnique();
        // How many a business has prepared and not sent, by when they expire; the cleanup goes by it too.
        builder.HasIndex(x => new { x.TenantId, x.Status, x.ExpiresAt });
        builder.HasIndex(x => x.ExpiresAt);
        // One order comes from one prepared order at most.
        builder.HasIndex(x => x.OrderId).IsUnique().HasFilter("[OrderId] IS NOT NULL");
    }
}
