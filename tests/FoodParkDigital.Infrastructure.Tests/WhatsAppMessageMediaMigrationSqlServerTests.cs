using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Domain.WhatsApp.Entities;
using FoodParkDigital.Domain.WhatsApp.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// AddWhatsAppMessageMedia (Fase 31L, ADR 0092): the photo or audio a customer sent by WhatsApp, in four nullable columns of the
/// messages with a check that keeps each row one the domain writes. The messages that exist bring none, the version before it
/// keeps storing messages, and rolling back stops with error 50061, without changes, once a photo or audio was saved; without
/// any, it drops only what it added.
/// </summary>
public class WhatsAppMessageMediaMigrationSqlServerTests
{
    // The migration right before it.
    private const string BeforeMigration = "20261112161350_AddOnlineOrderReminderAndReviewEmails";
    private const string Migration = "20261112165041_AddWhatsAppMessageMedia";

    [SqlServerFact]
    public async Task TheMessagesThatExist_ShouldBringNoMedia_AndTheCheckShouldKeepEachRowOneTheDomainWrites()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var chat = await SeedAsync(BeforeWhatsAppMessageMedia.CreateContext(database));

        await MigrateAsync(database, Migration);

        await using var context = database.CreateContext(chat.TenantId);
        var text = await context.WhatsAppMessages.AsNoTracking().SingleAsync(m => m.Id == chat.MessageId);
        Assert.Equal(((WhatsAppMediaKind?)null, (WhatsAppMediaState?)null, (string?)null), (text.MediaKind, text.MediaState, text.MediaPath));

        // A photo waiting, then saved; an audio given up: what the domain writes is kept.
        WhatsAppMessage photo, audio;
        await using (var add = database.CreateContext(chat.TenantId))
        {
            var conversation = await add.WhatsAppConversations.SingleAsync(c => c.Id == chat.ConversationId);
            photo = conversation.AddInboundMedia("📷 Foto", "wamid.foto", WhatsAppMediaKind.Image, "1029384756");
            audio = conversation.AddInboundMedia("🎤 Nota de voz", "wamid.audio", WhatsAppMediaKind.Audio, "1029384757");
            await add.SaveChangesAsync();
        }
        var path = $"/files/whatsapp/{chat.TenantId}/202611/{Guid.NewGuid()}.webp";
        await using (var keep = database.CreateContext(chat.TenantId))
        {
            Assert.True((await keep.WhatsAppMessages.SingleAsync(m => m.Id == photo.Id)).SaveMedia(path));
            Assert.True((await keep.WhatsAppMessages.SingleAsync(m => m.Id == audio.Id)).GiveUpMedia());
            await keep.SaveChangesAsync();
        }
        await using var check = database.CreateContext(chat.TenantId);
        var kept = await check.WhatsAppMessages.AsNoTracking().SingleAsync(m => m.Id == photo.Id);
        Assert.Equal((WhatsAppMediaKind.Image, "1029384756", WhatsAppMediaState.Saved, path), (kept.MediaKind, kept.MediaId, kept.MediaState, kept.MediaPath));
        Assert.Equal(WhatsAppMediaState.Unavailable, (await check.WhatsAppMessages.AsNoTracking().SingleAsync(m => m.Id == audio.Id)).MediaState);

        // The check refuses rows the domain never writes: a path without being saved, saved without a path, a kind without its id.
        foreach (var refused in new FormattableString[]
                 {
                     $"UPDATE [WhatsAppMessages] SET [MediaPath] = N'/files/x' WHERE [Id] = {audio.Id}",
                     $"UPDATE [WhatsAppMessages] SET [MediaPath] = NULL WHERE [Id] = {photo.Id}",
                     $"UPDATE [WhatsAppMessages] SET [MediaKind] = 1 WHERE [Id] = {chat.MessageId}",
                     $"UPDATE [WhatsAppMessages] SET [MediaKind] = 3 WHERE [Id] = {audio.Id}"
                 })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => check.Database.ExecuteSqlAsync(refused));
            Assert.Contains("CK_WhatsAppMessages_Media", error.Message);
        }
    }

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepStoringMessages_WithoutMedia()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);

        // The app as deployed before this version stores a conversation and a message, unaware of the columns.
        var chat = await SeedAsync(BeforeWhatsAppMessageMedia.CreateContext(database));

        await using var context = database.CreateContext(chat.TenantId);
        var message = await context.WhatsAppMessages.AsNoTracking().SingleAsync(m => m.Id == chat.MessageId);
        Assert.Equal(("📷 Foto", (WhatsAppMediaKind?)null, (WhatsAppMediaState?)null), (message.Body, message.MediaKind, message.MediaState));
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceAMediaWasSaved_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var chat = await SeedAsync(database.CreateContext());
        Guid saved, waiting;
        await using (var add = database.CreateContext(chat.TenantId))
        {
            var conversation = await add.WhatsAppConversations.SingleAsync(c => c.Id == chat.ConversationId);
            var photo = conversation.AddInboundMedia("📷 Foto", "wamid.uno", WhatsAppMediaKind.Image, "11");
            var audio = conversation.AddInboundMedia("🎤 Nota de voz", "wamid.dos", WhatsAppMediaKind.Audio, "12");
            photo.SaveMedia($"/files/whatsapp/{chat.TenantId}/202611/{Guid.NewGuid()}.webp");
            await add.SaveChangesAsync();
            (saved, waiting) = (photo.Id, audio.Id);
        }

        // A photo was saved: rolling back would lose where it is, so it stops before anything changes.
        var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeMigration));
        Assert.True(stopped is SqlException { Number: 50061 }, stopped?.Message ?? "it was not stopped");
        await using (var context = database.CreateContext(chat.TenantId))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.NotNull((await context.WhatsAppMessages.AsNoTracking().SingleAsync(m => m.Id == saved)).MediaPath);
        }

        // Without a saved one (the waiting one keeps its marker), it drops only the columns and their check.
        await using (var clear = database.CreateContext(chat.TenantId))
            await clear.WhatsAppMessages.Where(m => m.Id == saved).ExecuteDeleteAsync();
        await MigrateAsync(database, BeforeMigration);
        await using (var context = BeforeWhatsAppMessageMedia.CreateContext(database, chat.TenantId))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Null(await context.Database.SqlQuery<int?>($"SELECT COL_LENGTH(N'dbo.WhatsAppMessages', N'MediaPath') AS [Value]").SingleAsync());
            Assert.Equal(0, await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE [name] = N'CK_WhatsAppMessages_Media'").SingleAsync());
            var message = await context.WhatsAppMessages.AsNoTracking().SingleAsync(m => m.Id == waiting);
            Assert.Equal("🎤 Nota de voz", message.Body);
            Assert.Equal(2, await context.WhatsAppMessages.AsNoTracking().CountAsync());
        }

        // Rolling back again does nothing; forward again, nothing brings media.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(chat.TenantId);
        Assert.Null((await forward.WhatsAppMessages.AsNoTracking().SingleAsync(m => m.Id == waiting)).MediaKind);
    }

    private sealed record Chat(Guid TenantId, Guid ConversationId, Guid MessageId);

    /// <summary>A business with a conversation and the marker of a photo, saved by <paramref name="seed"/>.</summary>
    private static async Task<Chat> SeedAsync(ApplicationDbContext seed)
    {
        await using (seed)
        {
            var slug = Guid.NewGuid().ToString("N")[..10];
            var tenant = new Tenant($"Cocina {slug}", slug, BusinessType.Food, $"{slug}@demo.com");
            var conversation = new WhatsAppConversation(tenant.Id, "+5215512345678", "Valeria");
            var message = conversation.AddMessage(MessageDirection.Inbound, "📷 Foto", $"wamid.{slug}");
            seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Food), conversation);
            await seed.SaveChangesAsync();
            return new Chat(tenant.Id, conversation.Id, message.Id);
        }
    }

    private static async Task MigrateAsync(SqlServerTestDatabase database, string target)
    {
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static Task<string> LatestMigrationAsync(ApplicationDbContext context) =>
        context.Database.SqlQuery<string>($"""
            SELECT TOP (1) [MigrationId] AS [Value] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC
            """).SingleAsync();
}
