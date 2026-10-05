using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Application.WhatsApp.Commands;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Domain.WhatsApp.Entities;
using FoodParkDigital.Domain.WhatsApp.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>Meta's delivery statuses move the ticks of the chat and the result of each queued message of the business.</summary>
public class WhatsAppStatusesTests
{
    [Fact]
    public async Task Statuses_ShouldUpdateTheBusinessesMessages_AndNotAnotherBusinessesOnes()
    {
        await using var fixture = SalesTestFixture.Create();
        var other = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: "otro");
        var otherConversation = new WhatsAppConversation(other.Id, "+525511112222");
        otherConversation.AddMessage(MessageDirection.Outbound, "Su pedido va en camino", "wamid.other");
        fixture.Context.WhatsAppConversations.Add(otherConversation);
        await fixture.Context.SaveChangesAsync();

        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: "propio");
        var conversation = new WhatsAppConversation(tenant.Id, "+525533334444");
        conversation.AddMessage(MessageDirection.Outbound, "Tu pedido está listo", "wamid.chat");
        var reminder = new OutgoingMessage(tenant.Id, MessagePurpose.AppointmentReminder, "+525533334444", "Ana", "Hola Ana",
            null, "es_MX", [], SalesTestFixture.Now);
        reminder.MarkSent(SalesTestFixture.Now, "wamid.queued");
        fixture.Context.WhatsAppConversations.Add(conversation);
        fixture.Context.OutgoingMessages.Add(reminder);
        await fixture.Context.SaveChangesAsync();

        var changed = await Handler(fixture).Handle(new ApplyWhatsAppStatusesCommand(tenant.Id,
        [
            new WhatsAppStatusReport("wamid.chat", MessageStatus.Read, SalesTestFixture.Now.AddMinutes(2)),
            new WhatsAppStatusReport("wamid.queued", MessageStatus.Failed, SalesTestFixture.Now.AddMinutes(1), "131047"),
            // Reported to this business's number for a message of another business: not this business's to change.
            new WhatsAppStatusReport("wamid.other", MessageStatus.Read, SalesTestFixture.Now.AddMinutes(3))
        ]), CancellationToken.None);

        Assert.Equal(2, changed);
        var messages = await fixture.Context.WhatsAppMessages.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        Assert.Equal(MessageStatus.Read, messages.Single(m => m.ExternalMessageId == "wamid.chat").Status);
        Assert.Equal(MessageStatus.Sent, messages.Single(m => m.ExternalMessageId == "wamid.other").Status);
        var failed = await fixture.Context.OutgoingMessages.AsNoTracking().SingleAsync();
        Assert.Equal(OutgoingMessageStatus.Failed, failed.Status);
        Assert.Equal(ApplyWhatsAppStatusesCommandHandler.OutsideWindowError, failed.LastError);
    }

    [Fact]
    public async Task Statuses_ShouldOnlyBeAppliedInTheScopeOfTheirBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(fixture).Handle(
            new ApplyWhatsAppStatusesCommand(Guid.NewGuid(), [new WhatsAppStatusReport("wamid.x", MessageStatus.Read, null)]),
            CancellationToken.None));
    }

    [Theory]
    [InlineData("131047", ApplyWhatsAppStatusesCommandHandler.OutsideWindowError)]
    [InlineData("131026", "WhatsApp no pudo entregar el mensaje (código 131026).")]
    [InlineData(null, "WhatsApp no pudo entregar el mensaje.")]
    public void Failure_ShouldBeDescribedByItsCodeOnly(string? code, string expected)
    {
        Assert.Equal(expected, ApplyWhatsAppStatusesCommandHandler.DescribeFailure(code));
    }

    private static ApplyWhatsAppStatusesCommandHandler Handler(SalesTestFixture fixture)
        => new(fixture.Context, fixture.TenantService, fixture.Clock, NullLogger<ApplyWhatsAppStatusesCommandHandler>.Instance);
}
