using System.Text.Encodings.Web;
using System.Text.Json;
using FoodParkDigital.Domain.Assistants;
using FoodParkDigital.Domain.Assistants.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// The OAuth clients AI assistants register (Fase 32E, ADR 0097). Not of a business: anyone registers one, and it reads
/// nothing until an account allows it. Its addresses are one JSON column, read and written whole: printable ASCII without
/// quotes or backslashes, written as they are, so the column has the size of the most a client may register.
/// </summary>
public class AssistantClientConfiguration : IEntityTypeConfiguration<AssistantClient>
{
    private static readonly JsonSerializerOptions UrisJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly ValueConverter<List<string>, string> UrisConverter = new(
        uris => JsonSerializer.Serialize(uris, UrisJson),
        json => JsonSerializer.Deserialize<List<string>>(json, UrisJson) ?? new List<string>());

    private static readonly ValueComparer<List<string>> UrisComparer = new(
        (a, b) => a!.SequenceEqual(b!),
        uris => uris.Aggregate(0, (hash, uri) => HashCode.Combine(hash, uri.GetHashCode())),
        uris => uris.ToList());

    public void Configure(EntityTypeBuilder<AssistantClient> builder)
    {
        builder.ToTable("AssistantClients", table =>
            table.HasCheckConstraint("CK_AssistantClients_ClientId", $"LEN([ClientId]) = {AssistantSecrets.Length}"));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ClientId).HasMaxLength(AssistantSecrets.Length).IsUnicode(false).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(AssistantClient.MaxNameLength);
        builder.Property<List<string>>("_redirectUris")
            .HasColumnName("RedirectUris")
            .HasConversion(UrisConverter, UrisComparer)
            .HasMaxLength(AssistantClient.MaxRedirectUrisStoredLength)
            .IsUnicode(false)
            .IsRequired();
        builder.Ignore(x => x.RedirectUris);
        builder.Property(x => x.RegisteredAt).IsRequired();

        builder.HasIndex(x => x.ClientId).IsUnique();
        // The registrations of the last hour, and the cleanup of clients no account connected, go by when they were registered;
        // the cleanup of those connected long ago, by their last connection.
        builder.HasIndex(x => x.RegisteredAt);
        builder.HasIndex(x => x.LastConnectedAt).HasFilter("[LastConnectedAt] IS NOT NULL");
    }
}

/// <summary>
/// The codes a customer's permission gives (Fase 32E, ADR 0097): five minutes, one use, only their hash. They go with the
/// account and with their client; the connection a code made stays when the code is deleted.
/// </summary>
public class AssistantAuthorizationCodeConfiguration : IEntityTypeConfiguration<AssistantAuthorizationCode>
{
    public void Configure(EntityTypeBuilder<AssistantAuthorizationCode> builder)
    {
        builder.ToTable("AssistantAuthorizationCodes", table =>
        {
            table.HasCheckConstraint("CK_AssistantAuthorizationCodes_CodeHash", "LEN([CodeHash]) = 64");
            table.HasCheckConstraint("CK_AssistantAuthorizationCodes_CodeChallenge", $"LEN([CodeChallenge]) = {AssistantSecrets.Length}");
            table.HasCheckConstraint(
                "CK_AssistantAuthorizationCodes_Redeemed",
                "([RedeemedAt] IS NULL AND [ConnectionId] IS NULL) OR ([RedeemedAt] IS NOT NULL AND [ConnectionId] IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CodeHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(x => x.RedirectUri).HasMaxLength(AssistantClient.MaxRedirectUriLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.CodeChallenge).HasMaxLength(AssistantSecrets.Length).IsUnicode(false).IsRequired();
        builder.Property(x => x.Resource).HasMaxLength(AssistantAuthorizationCode.MaxResourceLength).IsRequired();
        builder.Property(x => x.Scope).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(x => x.IssuedAt).IsRequired();
        builder.Property(x => x.ExpiresAt).IsRequired();

        builder.HasOne<AssistantClient>()
            .WithMany()
            .HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        // The account already deletes both: a cascade from here too would be a second path.
        builder.HasOne<AssistantConnection>()
            .WithMany()
            .HasForeignKey(x => x.ConnectionId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => x.CodeHash).IsUnique();
        builder.HasIndex(x => x.ExpiresAt);
        builder.HasIndex(x => x.ClientId);
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.ConnectionId).HasFilter("[ConnectionId] IS NOT NULL");
    }
}

/// <summary>
/// The assistants an account connected (Fase 32E, ADR 0097). They belong to the account, not to a business, so they have no
/// business filter: every read goes by the account's id, or by a token of theirs. They go with the account; their client
/// is not deleted while one exists. One working connection per account and client.
/// </summary>
public class AssistantConnectionConfiguration : IEntityTypeConfiguration<AssistantConnection>
{
    public void Configure(EntityTypeBuilder<AssistantConnection> builder)
    {
        builder.ToTable("AssistantConnections");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.ClientId).IsRequired();
        builder.Property(x => x.Scope).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(x => x.RedirectUri).HasMaxLength(AssistantClient.MaxRedirectUriLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.Resource).HasMaxLength(AssistantAuthorizationCode.MaxResourceLength).IsRequired();
        builder.Property(x => x.GrantedAt).IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AssistantClient>()
            .WithMany()
            .HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        // The account's list, and the one working connection it has with each assistant.
        builder.HasIndex(x => new { x.UserId, x.ClientId }).IsUnique().HasFilter("[RevokedAt] IS NULL");
        builder.HasIndex(x => x.ClientId);
        // The cleanup of disconnected ones goes by when they stopped.
        builder.HasIndex(x => x.RevokedAt).HasFilter("[RevokedAt] IS NOT NULL");
    }
}

/// <summary>
/// The tokens of the connections (Fase 32E, ADR 0097): only their hash, looked up on every request to /mcp/cuenta. They go
/// with their connection.
/// </summary>
public class AssistantTokenConfiguration : IEntityTypeConfiguration<AssistantToken>
{
    public void Configure(EntityTypeBuilder<AssistantToken> builder)
    {
        builder.ToTable("AssistantTokens", table =>
        {
            table.HasCheckConstraint("CK_AssistantTokens_TokenHash", "LEN([TokenHash]) = 64");
            table.HasCheckConstraint("CK_AssistantTokens_Kind", "[Kind] IN (1, 2)");
            table.HasCheckConstraint("CK_AssistantTokens_RotatedAt", "[RotatedAt] IS NULL OR [Kind] = 2");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ConnectionId).IsRequired();
        builder.Property(x => x.Kind).HasConversion<int>();
        builder.Property(x => x.TokenHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(x => x.IssuedAt).IsRequired();
        builder.Property(x => x.ExpiresAt).IsRequired();

        builder.HasOne<AssistantConnection>()
            .WithMany()
            .HasForeignKey(x => x.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.ConnectionId);
        builder.HasIndex(x => x.ExpiresAt);
    }
}
