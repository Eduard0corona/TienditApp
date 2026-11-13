using System.Text.Json;
using System.Text.Json.Serialization;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// The carts customer accounts leave in the stores (Fase 31J, ADR 0090). They belong to the account, not to a business, like
/// the favorites, so they have no business filter: every read goes by the account's own id. They go with the account; a
/// business is never deleted (it is suspended or cancelled). The lines are one JSON column: the cart is read and written
/// whole, and never searched by what it has.
/// </summary>
public class AccountCartConfiguration : IEntityTypeConfiguration<AccountCart>
{
    public void Configure(EntityTypeBuilder<AccountCart> builder)
    {
        builder.ToTable("AccountCarts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.BusinessId).IsRequired();
        builder.Property(x => x.SavedAt).IsRequired();
        builder.Property<List<AccountCartLine>>("_lines")
            .HasColumnName("Lines")
            .HasConversion(AccountCartLinesStorage.Converter, AccountCartLinesStorage.Comparer)
            .IsUnicode()
            .IsRequired();
        builder.Ignore(x => x.Lines);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.BusinessId)
            .OnDelete(DeleteBehavior.Restrict);

        // One cart per store and account; it is also the account's list.
        builder.HasIndex(x => new { x.UserId, x.BusinessId }).IsUnique();
    }
}

/// <summary>The lines of a cart as JSON: product, options, quantity and note, with short names (50 lines fit easily).</summary>
internal static class AccountCartLinesStorage
{
    private static readonly JsonSerializerOptions Options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static readonly ValueConverter<List<AccountCartLine>, string> Converter = new(
        lines => JsonSerializer.Serialize(lines.Select(StoredLine.From).ToList(), Options),
        stored => (JsonSerializer.Deserialize<List<StoredLine>>(stored, Options) ?? new List<StoredLine>())
            .Select(line => line.ToLine())
            .ToList());

    public static readonly ValueComparer<List<AccountCartLine>> Comparer = new(
        (a, b) => a != null && b != null && a.SequenceEqual(b),
        lines => lines.Aggregate(0, (hash, line) => HashCode.Combine(hash, line.GetHashCode())),
        lines => lines.ToList());

    private sealed record StoredLine(
        [property: JsonPropertyName("p")] Guid ProductId,
        [property: JsonPropertyName("o")] List<Guid> OptionIds,
        [property: JsonPropertyName("q")] int Quantity,
        [property: JsonPropertyName("n")] string? Notes)
    {
        public static StoredLine From(AccountCartLine line) => new(line.ProductId, line.OptionIds.ToList(), line.Quantity, line.Notes);

        public AccountCartLine ToLine() => new(ProductId, OptionIds ?? [], Quantity, Notes);
    }
}
