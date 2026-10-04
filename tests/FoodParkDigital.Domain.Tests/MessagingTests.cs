using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;

namespace FoodParkDigital.Domain.Tests;

public class MessagingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Placeholders_ShouldBecomeParametersInTheOrderTheyFirstAppear()
    {
        const string body = "Hola {nombre}, {negocio} te espera. ¡Gracias, {nombre}!";
        var values = new Dictionary<string, string> { ["nombre"] = "Ana", ["negocio"] = "Café Luna" };

        Assert.Equal(["nombre", "negocio"], MessageTemplateText.GetPlaceholders(body));
        Assert.Equal(["Ana", "Café Luna"], MessageTemplateText.Parameters(body, values));
        Assert.Equal("Hola Ana, Café Luna te espera. ¡Gracias, Ana!", MessageTemplateText.Render(body, values));
    }

    [Fact]
    public void CampaignTemplate_ShouldRejectReminderAndUnknownPlaceholders()
    {
        var reminderOnly = Assert.Throws<DomainException>(() =>
            new MessageTemplate(Guid.NewGuid(), "Promo", MessagePurpose.Campaign, "Tu cita es el {fecha}"));
        var unknown = Assert.Throws<DomainException>(() =>
            new MessageTemplate(Guid.NewGuid(), "Promo", MessagePurpose.Campaign, "Usa el código {cupon}"));

        Assert.Contains("{fecha}", reminderOnly.Message);
        Assert.Contains("{cupon}", unknown.Message);
        var reminder = new MessageTemplate(Guid.NewGuid(), "Recordatorio", MessagePurpose.AppointmentReminder,
            "Hola {nombre}, tu cita de {servicio} es el {fecha} a las {hora}.", "cita_recordatorio");
        Assert.Equal("cita_recordatorio", reminder.ProviderTemplateName);
        Assert.Equal(MessageTemplate.DefaultLanguageCode, reminder.LanguageCode);
    }

    [Theory]
    [InlineData("Recordatorio Cita")]
    [InlineData("cita-1")]
    public void ProviderTemplateName_ShouldFollowMetaRules(string providerName)
    {
        Assert.Throws<DomainException>(() =>
            new MessageTemplate(Guid.NewGuid(), "Promo", MessagePurpose.Campaign, "Hola {nombre}", providerName));
    }

    [Fact]
    public void RecipientKey_ShouldMatchWhatsAppAndTypedMexicanMobiles()
    {
        Assert.Equal("+525512345678", MessagingPhone.ToKey("+5215512345678"));
        Assert.Equal("+525512345678", MessagingPhone.ToKey("+525512345678"));
        Assert.Equal("+14155550100", MessagingPhone.ToKey("+14155550100"));
    }

    [Fact]
    public void OptOut_ShouldKeepItsHistoryWhenTheCustomerComesBack()
    {
        var optOut = new MessagingOptOut(Guid.NewGuid(), "+5215512345678", OptOutSource.CustomerKeyword, Now);
        optOut.Resubscribe(Now.AddDays(1));
        optOut.Resubscribe(Now.AddDays(2));

        Assert.Equal("+525512345678", optOut.Phone);
        Assert.False(optOut.IsActive);
        Assert.Equal(Now.AddDays(1), optOut.ResubscribedAt);

        optOut.OptOut(OptOutSource.Staff, Now.AddDays(3));
        Assert.True(optOut.IsActive);
        Assert.Equal(OptOutSource.Staff, optOut.Source);
        Assert.Equal(Now.AddDays(3), optOut.OptedOutAt);
    }

    [Fact]
    public void Campaign_WithoutRecipients_ShouldCompleteWhenItStarts()
    {
        var campaign = new MessageCampaign(Guid.NewGuid(), "Lunes 2x1", Guid.NewGuid(), CampaignAudienceKind.Buyers, 2, 30, Now);
        var token = campaign.LockToken;

        campaign.Start(Now, recipientCount: 0, excludedCount: 3);

        Assert.Equal(CampaignStatus.Completed, campaign.Status);
        Assert.Equal(Now, campaign.CompletedAt);
        Assert.Equal(3, campaign.ExcludedCount);
        Assert.NotEqual(token, campaign.LockToken);
        Assert.Throws<DomainException>(() => campaign.Cancel(Now));
    }

    [Fact]
    public void LapsedAudience_ShouldNeedTheDaysWithoutBuying()
    {
        Assert.Throws<DomainException>(() =>
            new MessageCampaign(Guid.NewGuid(), "Te extrañamos", Guid.NewGuid(), CampaignAudienceKind.Lapsed, 1, null, Now));

        var everyone = new MessageCampaign(Guid.NewGuid(), "Todos", Guid.NewGuid(), CampaignAudienceKind.AllCustomers, 99, 10, Now);
        Assert.Equal(1, everyone.AudienceMinimumOrders);
        Assert.Null(everyone.AudienceDays);
    }

    [Fact]
    public void LateDeliveryFailure_ShouldOnlyApplyToMessagesNotDelivered()
    {
        var delivered = NewMessage();
        delivered.MarkSent(Now, "wamid.1");
        delivered.RecordDelivery(Now.AddSeconds(5));
        delivered.RecordDeliveryFailure("WhatsApp no pudo entregar el mensaje.");

        var undelivered = NewMessage();
        undelivered.MarkSent(Now, "wamid.2");
        undelivered.RecordDeliveryFailure("WhatsApp no pudo entregar el mensaje (código 131026).");

        Assert.Equal(OutgoingMessageStatus.Sent, delivered.Status);
        Assert.Equal(OutgoingMessageStatus.Failed, undelivered.Status);
        Assert.Contains("131026", undelivered.LastError);
    }

    private static OutgoingMessage NewMessage()
        => new(Guid.NewGuid(), MessagePurpose.Campaign, "+525512345678", "Ana", "Hola Ana", null, "es_MX", [], Now);
}
