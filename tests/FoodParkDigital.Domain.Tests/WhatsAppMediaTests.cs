using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.WhatsApp.Entities;
using FoodParkDigital.Domain.WhatsApp.Enums;
using FoodParkDigital.Domain.WhatsApp.Events;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The photos and audios customers send by WhatsApp (Fase 31L, ADR 0092): the message keeps its marker and what to download,
/// its business is told to download it, and it ends kept or given up, once.
/// </summary>
public class WhatsAppMediaTests
{
    private static readonly Guid Business = Guid.NewGuid();

    [Fact]
    public void APhotoOfTheCustomer_ShouldWaitToBeDownloaded_AndTellItsBusiness()
    {
        var conversation = new WhatsAppConversation(Business, "+5215512345678", "Valeria");

        var message = conversation.AddInboundMedia("📷 Foto: así llegó", "wamid.1", WhatsAppMediaKind.Image, "1234567890");

        Assert.Equal(
            (WhatsAppMediaKind.Image, "1234567890", WhatsAppMediaState.Pending, (string?)null),
            (message.MediaKind, message.MediaId, message.MediaState, message.MediaPath));
        Assert.Equal(("📷 Foto: así llegó", MessageDirection.Inbound, 1), (message.Body, message.Direction, conversation.UnreadCount));
        Assert.Equal("📷 Foto: así llegó", conversation.LastMessageBody);
        var received = Assert.IsType<WhatsAppMediaReceivedEvent>(Assert.Single(conversation.DomainEvents));
        Assert.Equal((message.Id, Business), (received.MessageId, received.TenantId));
    }

    [Fact]
    public void AMediaThatIsNotValid_ShouldBeRefused_BeforeTheConversationChanges()
    {
        var conversation = new WhatsAppConversation(Business, "+5215512345678");

        foreach (var mediaId in new[] { "", "../otro", "id con espacio", new string('1', WhatsAppMessage.MaxMediaIdLength + 1) })
            Assert.Throws<DomainException>(() => conversation.AddInboundMedia("🎤 Nota de voz", "wamid.2", WhatsAppMediaKind.Audio, mediaId));
        Assert.Throws<DomainException>(() => conversation.AddInboundMedia("🎬 Video", "wamid.3", (WhatsAppMediaKind)9, "123"));

        Assert.Empty(conversation.Messages);
        Assert.Empty(conversation.DomainEvents);
        Assert.Equal(0, conversation.UnreadCount);
        Assert.True(WhatsAppMessage.IsMediaId(new string('7', WhatsAppMessage.MaxMediaIdLength)));
    }

    [Fact]
    public void AMediaKept_ShouldStaySo_AndOneGivenUpToo()
    {
        var conversation = new WhatsAppConversation(Business, "+5215512345678");
        var kept = conversation.AddInboundMedia("🎤 Nota de voz", "wamid.4", WhatsAppMediaKind.Audio, "555");
        var lost = conversation.AddInboundMedia("📷 Foto", "wamid.5", WhatsAppMediaKind.Image, "556");
        var path = $"/files/whatsapp/{Business}/202611/{Guid.NewGuid()}.ogg";

        Assert.True(kept.SaveMedia(path));
        Assert.True(lost.GiveUpMedia());

        Assert.Equal((WhatsAppMediaState.Saved, path), (kept.MediaState, kept.MediaPath));
        Assert.Equal((WhatsAppMediaState.Unavailable, (string?)null), (lost.MediaState, lost.MediaPath));
        // Once: a later download or failure changes nothing.
        Assert.False(kept.GiveUpMedia());
        Assert.False(kept.SaveMedia("/files/otro"));
        Assert.False(lost.SaveMedia(path));
        Assert.Equal((WhatsAppMediaState.Saved, path), (kept.MediaState, kept.MediaPath));
        Assert.Equal(WhatsAppMediaState.Unavailable, lost.MediaState);
    }

    [Fact]
    public void ATextOrAMessageOfTheBusiness_ShouldHaveNoMedia()
    {
        var conversation = new WhatsAppConversation(Business, "+5215512345678");
        var text = conversation.AddMessage(MessageDirection.Inbound, "Hola", "wamid.6");
        var reply = conversation.AddMessage(MessageDirection.Outbound, "¡Hola!", "wamid.7");

        Assert.Equal(((WhatsAppMediaKind?)null, (WhatsAppMediaState?)null), (text.MediaKind, text.MediaState));
        Assert.False(text.SaveMedia("/files/x"));
        Assert.False(reply.GiveUpMedia());
        Assert.Null(reply.MediaState);
        Assert.Empty(conversation.DomainEvents);
    }

    [Fact]
    public void APathThatIsNotValid_ShouldBeRefused()
    {
        var message = new WhatsAppConversation(Business, "+5215512345678")
            .AddInboundMedia("📷 Foto", "wamid.8", WhatsAppMediaKind.Image, "557");

        Assert.Throws<DomainException>(() => message.SaveMedia(" "));
        Assert.Throws<DomainException>(() => message.SaveMedia("/" + new string('a', WhatsAppMessage.MaxMediaPathLength)));
        Assert.Equal(WhatsAppMediaState.Pending, message.MediaState);
    }
}
