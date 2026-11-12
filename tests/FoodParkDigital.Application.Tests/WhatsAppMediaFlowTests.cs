using System.Text;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.OnlineBooking;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Application.WhatsApp.Commands;
using FoodParkDigital.Application.WhatsApp.DTOs;
using FoodParkDigital.Application.WhatsApp.Media;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Domain.WhatsApp.Entities;
using FoodParkDigital.Domain.WhatsApp.Enums;
using FoodParkDigital.Domain.WhatsApp.Events;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The photos and audios customers send by WhatsApp (Fase 31L, ADR 0092): the message waits for them, the outbox downloads each
/// one, keeps it privately in its business's folder (a photo saved again as WebP without its metadata, an audio as it came
/// only if a browser plays it) and tells the chats; what cannot be kept leaves only its marker; and only whoever runs the chat
/// of that business opens them.
/// </summary>
public class WhatsAppMediaFlowTests
{
    [Fact]
    public async Task APhoto_ShouldBeKeptAsWebpWithoutItsMetadata_InItsBusinessFolder_AndTheChatsTold()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenantId, message) = await MediaMessageAsync(fixture, WhatsAppMediaKind.Image);
        var storage = new MemoryStorage();
        var notifications = new Notifications();

        await HandleAsync(fixture, tenantId, message, new Downloader(Jpeg(2400, 1200)), storage, notifications);

        var (key, bytes, contentType) = Assert.Single(storage.Saved);
        Assert.Matches($"^whatsapp/{tenantId}/{fixture.Clock.UtcNow:yyyyMM}/[0-9a-f-]{{36}}\\.webp$", key);
        Assert.Equal("image/webp", contentType);
        using (var kept = Image.Load(bytes))
        {
            Assert.Equal((WhatsAppMediaRules.MaxImageDimension, 800), (kept.Width, kept.Height));
            Assert.Null(kept.Metadata.ExifProfile);
        }
        var stored = await StoredAsync(fixture, message);
        Assert.Equal((WhatsAppMediaState.Saved, "/files/" + key), (stored.MediaState, stored.MediaPath));
        var told = Assert.Single(notifications.Media);
        Assert.Equal((tenantId, stored.ConversationId, message, "Saved", "/files/" + key), told);
    }

    [Fact]
    public async Task AVoiceNote_ShouldBeKeptAsItCame_OnlyWhenABrowserPlaysIt()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenantId, voice) = await MediaMessageAsync(fixture, WhatsAppMediaKind.Audio);
        var (_, amr) = await MediaMessageAsync(fixture, WhatsAppMediaKind.Audio, tenantId);
        var storage = new MemoryStorage();
        var ogg = "OggS\0\u0002\0\0\0\0\0\0opus"u8.ToArray();

        await HandleAsync(fixture, tenantId, voice, new Downloader(ogg), storage);
        await HandleAsync(fixture, tenantId, amr, new Downloader("#!AMR\n\u0001\u0002"u8.ToArray()), storage);

        var (key, bytes, contentType) = Assert.Single(storage.Saved);
        Assert.EndsWith(".ogg", key);
        Assert.Equal("audio/ogg", contentType);
        Assert.Equal(ogg, bytes);
        Assert.Equal(WhatsAppMediaState.Saved, (await StoredAsync(fixture, voice)).MediaState);
        Assert.Equal((WhatsAppMediaState.Unavailable, (string?)null), ((await StoredAsync(fixture, amr)).MediaState, (await StoredAsync(fixture, amr)).MediaPath));
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("empty")]
    [InlineData("unavailable")]
    [InlineData("too-big")]
    [InlineData("broken")]
    public async Task WhatCannotBeKept_ShouldLeaveOnlyItsMarker_AndTheChatsTold(string what)
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenantId, message) = await MediaMessageAsync(fixture, WhatsAppMediaKind.Image);
        var storage = new MemoryStorage();
        var notifications = new Notifications();
        var downloader = what switch
        {
            "pdf" => new Downloader("%PDF-1.7 no es una foto"u8.ToArray()),
            "empty" => new Downloader([]),
            "too-big" => new Downloader(new byte[WhatsAppMediaRules.MaxImageBytes + 1]),
            // A JPEG header with nothing after it: it does not decode.
            "broken" => new Downloader([0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10]),
            _ => new Downloader(WhatsAppMediaDownload.Unavailable)
        };

        await HandleAsync(fixture, tenantId, message, downloader, storage, notifications);

        Assert.Empty(storage.Saved);
        var stored = await StoredAsync(fixture, message);
        Assert.Equal((WhatsAppMediaState.Unavailable, (string?)null, "📷 Foto"), (stored.MediaState, stored.MediaPath, stored.Body));
        Assert.Equal("Unavailable", Assert.Single(notifications.Media).State);
        // A photo is never asked bigger than WhatsApp takes.
        Assert.Equal(WhatsAppMediaRules.MaxImageBytes, downloader.AskedMax);
    }

    [Fact]
    public async Task WhatsAppNotAnsweringNow_ShouldBeTriedAgain_UntilTheLastAttemptGivesUp()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenantId, message) = await MediaMessageAsync(fixture, WhatsAppMediaKind.Audio);
        var storage = new MemoryStorage();

        await Assert.ThrowsAsync<WhatsAppMediaNotReadyException>(
            () => HandleAsync(fixture, tenantId, message, new Downloader(WhatsAppMediaDownload.Transient), storage));
        Assert.Equal(WhatsAppMediaState.Pending, (await StoredAsync(fixture, message)).MediaState);

        // Every photo being read at once: also later.
        var (_, photo) = await MediaMessageAsync(fixture, WhatsAppMediaKind.Image, tenantId);
        var busy = new PublicPhotoGate(1, TimeSpan.Zero);
        using (await busy.EnterAsync(CancellationToken.None))
            await Assert.ThrowsAsync<WhatsAppMediaNotReadyException>(
                () => HandleAsync(fixture, tenantId, photo, new Downloader(Jpeg(10, 10)), storage, gate: busy));
        Assert.Equal(WhatsAppMediaState.Pending, (await StoredAsync(fixture, photo)).MediaState);

        // The last attempt: the chat stops waiting for it.
        await HandleAsync(fixture, tenantId, message, new Downloader(WhatsAppMediaDownload.Transient), storage, lastAttempt: true);
        Assert.Equal(WhatsAppMediaState.Unavailable, (await StoredAsync(fixture, message)).MediaState);
        Assert.Empty(storage.Saved);

        // The storage timing out is tried again too, and on the last attempt it gives up as well: never «Descargando…» forever.
        var slowStorage = new MemoryStorage { TimesOut = true };
        var ogg = "OggS\0\u0002"u8.ToArray();
        var (_, late) = await MediaMessageAsync(fixture, WhatsAppMediaKind.Audio, tenantId);
        await Assert.ThrowsAsync<TaskCanceledException>(() => HandleAsync(fixture, tenantId, late, new Downloader(ogg), slowStorage));
        Assert.Equal(WhatsAppMediaState.Pending, (await StoredAsync(fixture, late)).MediaState);
        await HandleAsync(fixture, tenantId, late, new Downloader(ogg), slowStorage, lastAttempt: true);
        Assert.Equal(WhatsAppMediaState.Unavailable, (await StoredAsync(fixture, late)).MediaState);
    }

    [Fact]
    public async Task AMediaKeptAlready_OrOfAnotherBusiness_ShouldNotBeDownloadedAgain()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenantId, message) = await MediaMessageAsync(fixture, WhatsAppMediaKind.Audio);
        var (otherId, theirs) = await MediaMessageAsync(fixture, WhatsAppMediaKind.Audio);
        var ogg = "OggS\0\u0002"u8.ToArray();
        var storage = new MemoryStorage();
        await HandleAsync(fixture, tenantId, message, new Downloader(ogg), storage);

        var again = new Downloader(ogg);
        await HandleAsync(fixture, tenantId, message, again, storage);
        // The event of another business's message, run in this one's scope: nothing.
        await HandleAsync(fixture, tenantId, theirs, again, storage, eventTenant: otherId);
        await HandleAsync(fixture, tenantId, theirs, again, storage);

        Assert.Equal(0, again.Calls);
        Assert.Single(storage.Saved);
        fixture.TenantService.SetTenant(otherId);
        Assert.Equal(WhatsAppMediaState.Pending, (await StoredAsync(fixture, theirs)).MediaState);
    }

    [Fact]
    public async Task TheFile_ShouldOpenOnlyForItsBusiness_ForWhoeverRunsTheChat()
    {
        var tenantId = Guid.NewGuid();
        var key = $"whatsapp/{tenantId}/202611/{Guid.NewGuid()}.ogg";
        var storage = new MemoryStorage();
        await storage.SaveAsync(key, new MemoryStream("OggS"u8.ToArray()), "audio/ogg");

        var opened = await Query(tenantId, storage).Handle(new GetWhatsAppMediaFileQuery(key), CancellationToken.None);
        Assert.True(opened.IsSuccess, opened.Error);
        Assert.Equal(("audio/ogg", Path.GetFileName(key)), (opened.Value!.ContentType, opened.Value.FileName));

        // Another business's, a key the app never creates, or one that is gone: like a missing one.
        foreach (var other in new[]
                 {
                     key.Replace(tenantId.ToString(), Guid.NewGuid().ToString()),
                     $"whatsapp/{tenantId}/202611/../{Guid.NewGuid()}.ogg",
                     $"whatsapp/{tenantId}/202611/{Guid.NewGuid()}.exe",
                     $"whatsapp/{tenantId}/202611/{Guid.NewGuid()}.webp"
                 })
            Assert.Equal(GetPrivateOrderFileQueryHandler.NotFoundError, (await Query(tenantId, storage).Handle(new GetWhatsAppMediaFileQuery(other), CancellationToken.None)).Error);
        var permission = Assert.Single(typeof(GetWhatsAppMediaFileQuery).GetCustomAttributes(typeof(RequiresPermissionAttribute), false).Cast<RequiresPermissionAttribute>());
        Assert.Equal([TenantPermission.ManageWhatsApp], permission.AnyOf);
    }

    [Fact]
    public async Task AnInboundPhoto_ShouldWaitInTheChat_AndAnIdThatIsNotOfWhatsAppStayAMarker()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        fixture.TenantService.SetTenant(tenant.Id);

        var photo = await ReceiveAsync(fixture, tenant.Id, "wamid.photo", "📷 Foto: así quiero el pastel", WhatsAppMediaKind.Image, "1029384756");
        var odd = await ReceiveAsync(fixture, tenant.Id, "wamid.odd", "📷 Foto", WhatsAppMediaKind.Image, "../otro");

        Assert.Equal(("Image", "Pending", (string?)null), (photo.MediaKind, photo.MediaState, photo.MediaPath));
        Assert.Equal(((string?)null, (string?)null), (odd.MediaKind, odd.MediaState));
        var stored = await fixture.Context.WhatsAppMessages.AsNoTracking().SingleAsync(m => m.Id == photo.Id);
        Assert.Equal("1029384756", stored.MediaId);
    }

    [Fact]
    public void TheRules_ShouldKnowTheAudioABrowserPlays_AndTheKeysTheAppCreates()
    {
        Assert.Equal((".ogg", "audio/ogg"), WhatsAppMediaRules.DetectAudio("OggS\0"u8));
        Assert.Equal((".mp3", "audio/mpeg"), WhatsAppMediaRules.DetectAudio("ID3\u0004"u8));
        Assert.Equal((".mp3", "audio/mpeg"), WhatsAppMediaRules.DetectAudio([0xFF, 0xFB, 0x90]));
        Assert.Equal((".m4a", "audio/mp4"), WhatsAppMediaRules.DetectAudio("\0\0\0 ftypM4A "u8));
        Assert.Equal((".aac", "audio/aac"), WhatsAppMediaRules.DetectAudio([0xFF, 0xF1, 0x50]));
        Assert.Null(WhatsAppMediaRules.DetectAudio("#!AMR\n"u8));
        Assert.Null(WhatsAppMediaRules.DetectAudio("%PDF-"u8));

        var tenantId = Guid.NewGuid();
        Assert.True(WhatsAppMediaRules.TryParseKey($"whatsapp/{tenantId}/202611/{Guid.NewGuid()}.m4a", out var parsed));
        Assert.Equal(tenantId, parsed);
        Assert.False(WhatsAppMediaRules.TryParseKey($"orders/{tenantId}/service-intake/202611/{Guid.NewGuid()}.webp", out _));
        Assert.False(WhatsAppMediaRules.TryParseKey($"whatsapp/{tenantId}/2026/{Guid.NewGuid()}.webp", out _));
        Assert.Equal(WhatsAppMediaRules.MaxAudioBytes, WhatsAppMediaRules.MaxBytes(WhatsAppMediaKind.Audio));
    }

    private static async Task<(Guid TenantId, Guid MessageId)> MediaMessageAsync(
        SalesTestFixture fixture, WhatsAppMediaKind kind, Guid? tenantId = null)
    {
        var business = tenantId ?? (await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food)).Id;
        fixture.TenantService.SetTenant(business);
        var conversation = await fixture.Context.WhatsAppConversations.FirstOrDefaultAsync(c => c.TenantId == business);
        if (conversation is null)
        {
            conversation = new WhatsAppConversation(business, "+5215512345678", "Valeria");
            fixture.Context.WhatsAppConversations.Add(conversation);
        }
        var message = conversation.AddInboundMedia(
            kind == WhatsAppMediaKind.Image ? "📷 Foto" : "🎤 Nota de voz", $"wamid.{Guid.NewGuid():N}", kind, Random.Shared.Next(1, int.MaxValue).ToString());
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return (business, message.Id);
    }

    private static async Task HandleAsync(
        SalesTestFixture fixture, Guid tenantId, Guid messageId, Downloader downloader, MemoryStorage storage,
        Notifications? notifications = null, PublicPhotoGate? gate = null, bool lastAttempt = false, Guid? eventTenant = null)
    {
        fixture.TenantService.SetTenant(tenantId);
        await new WhatsAppMediaDownloadHandler(
                fixture.Context, downloader, storage, new ImageProcessingService(), gate ?? new PublicPhotoGate(),
                notifications ?? new Notifications(), fixture.Clock, NullLogger<WhatsAppMediaDownloadHandler>.Instance)
            .HandleAsync(
                new WhatsAppMediaReceivedEvent(messageId, eventTenant ?? tenantId),
                new OutboxEventContext(Guid.NewGuid(), tenantId, null, null, fixture.Clock.UtcNow, lastAttempt ? 5 : 1, 5),
                CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
    }

    private static Task<WhatsAppMessage> StoredAsync(SalesTestFixture fixture, Guid messageId)
        => fixture.Context.WhatsAppMessages.AsNoTracking().SingleAsync(m => m.Id == messageId);

    private static GetWhatsAppMediaFileQueryHandler Query(Guid tenantId, IPrivateFileStorage storage)
    {
        var tenant = new InMemoryTenant();
        tenant.SetTenant(tenantId);
        return new GetWhatsAppMediaFileQueryHandler(tenant, storage);
    }

    private static async Task<ChatMessageDto> ReceiveAsync(
        SalesTestFixture fixture, Guid tenantId, string messageId, string body, WhatsAppMediaKind kind, string mediaId)
    {
        var result = await new ProcessInboundMessageCommandHandler(
                fixture.Context,
                fixture.TenantService,
                new NoReply(),
                new NoWhatsApp(),
                new Notifications(),
                NullLogger<ProcessInboundMessageCommandHandler>.Instance,
                clock: fixture.Clock)
            .Handle(new ProcessInboundMessageCommand
            {
                TenantId = tenantId,
                FromPhone = "+5215512345678",
                ProfileName = "Valeria",
                Body = body,
                ExternalMessageId = messageId,
                IsMedia = true,
                MediaKind = kind,
                MediaId = mediaId
            }, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    private static byte[] Jpeg(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(30, 120, 200));
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Make, "Teléfono del cliente");
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return stream.ToArray();
    }

    private sealed class Downloader : IWhatsAppMediaDownloader
    {
        private readonly WhatsAppMediaDownload _download;

        public Downloader(byte[] content) => _download = new WhatsAppMediaDownload(WhatsAppMediaDownloadOutcome.Downloaded, content);

        public Downloader(WhatsAppMediaDownload download) => _download = download;

        public int Calls { get; private set; }

        public long? AskedMax { get; private set; }

        public Task<WhatsAppMediaDownload> DownloadAsync(string mediaId, long maxBytes, CancellationToken cancellationToken)
        {
            Calls++;
            AskedMax = maxBytes;
            return Task.FromResult(_download);
        }
    }

    private sealed class MemoryStorage : IPrivateFileStorage
    {
        private readonly Dictionary<string, byte[]> _files = new();

        public List<(string Key, byte[] Bytes, string ContentType)> Saved { get; } = [];

        /// <summary>Its SDK gives up waiting, as a storage that does not answer.</summary>
        public bool TimesOut { get; init; }

        public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            if (TimesOut)
                throw new TaskCanceledException("The storage did not answer in time.");
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            _files[key] = buffer.ToArray();
            Saved.Add((key, buffer.ToArray(), contentType));
        }

        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream?>(_files.TryGetValue(key, out var bytes) ? new MemoryStream(bytes) : null);
    }

    private sealed class Notifications : IWhatsAppNotificationService
    {
        public List<(Guid TenantId, Guid ConversationId, Guid MessageId, string State, string? Path)> Media { get; } = [];

        public Task NotifyNewMessage(Guid tenantId, ConversationDto conversation, ChatMessageDto message) => Task.CompletedTask;

        public Task NotifyConversationUpdated(Guid tenantId, ConversationDto conversation) => Task.CompletedTask;

        public Task NotifyMessageMedia(Guid tenantId, Guid conversationId, Guid messageId, string mediaState, string? mediaPath)
        {
            Media.Add((tenantId, conversationId, messageId, mediaState, mediaPath));
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryTenant : ICurrentTenantService
    {
        public Guid TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class NoReply : IAiAgentAutoReplyService
    {
        public Task<string?> BuildReplyAsync(Guid tenantId, string incomingMessage, string? customerName, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    private sealed class NoWhatsApp : IWhatsAppService
    {
        public Task<Common.Models.MessageSendResult> SendTemplateMessageAsync(Guid tenantId, string phoneNumber, string templateName, string languageCode, string[] parameters, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<Common.Models.MessageSendResult> SendTextMessageAsync(Guid tenantId, string phoneNumber, string message, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
