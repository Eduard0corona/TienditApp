using FoodParkDigital.Domain.WhatsApp.Entities;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests.Support;

/// <summary>
/// SQL Server writes a new rowversion on every insert and update of a conversation; SQLite has no such
/// type. A random value on insert and a trigger that replaces it on every update make a save based on a
/// stale copy fail with <see cref="DbUpdateConcurrencyException"/>, as in production.
/// </summary>
internal static class SqliteConversationVersion
{
    public static void Configure(ModelBuilder modelBuilder)
        => modelBuilder.Entity<WhatsAppConversation>(conversation =>
        {
            conversation.Property<byte[]>("Version").HasDefaultValueSql("randomblob(8)");

            // RETURNING reports the row before AFTER triggers run, so EF would keep the old version and
            // the next save of the same context would conflict with itself; read it back afterwards.
            conversation.ToTable(table => table.UseSqlReturningClause(false));
        });

    /// <summary>Creates the trigger; call it after the schema exists.</summary>
    public static Task CreateTriggerAsync(DbContext context)
        => context.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER "WhatsAppConversations_Version" AFTER UPDATE ON "WhatsAppConversations"
            BEGIN
                UPDATE "WhatsAppConversations" SET "Version" = randomblob(8) WHERE "Id" = NEW."Id";
            END;
            """);
}
