using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The customer answers the quote from its link (Fase 29G, ADR 0061): the version sent and still valid, once, with what they
/// want to tell the business. Only they answer «from its link»: the business records the other channels.
/// </summary>
public class QuoteLinkReplyTests
{
    private static readonly Guid Business = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 2);
    private static readonly QuoteCustomer Valeria = new(null, "Valeria Ruiz", "+525522107765", null);

    private static Quote Sent(int validityDays = 15)
    {
        var quote = Quote.Start(Business, Folios.QuoteNumber(7), Valeria, null, true, validityDays, Now);
        quote.ReplaceLines([new QuoteLineInput(null, "Cambio de calentador", 1m, 1160m, VatRate.General)], Now);
        quote.Send(Today, new string('a', 43), null, Now);
        return quote;
    }

    [Fact]
    public void FromItsLink_TheCustomer_ShouldAcceptIt_WithWhatTheyWantToSay()
    {
        var quote = Sent();

        Assert.True(quote.ReplyFromLink(accepted: true, Today, "  Los espero el jueves  ", Now.AddHours(2)));

        Assert.Equal((QuoteStatus.Accepted, QuoteReplyChannel.Link, "Los espero el jueves"),
            (quote.Status, quote.ReplyChannel!.Value, quote.CustomerResponse));
        Assert.Equal((Now.AddHours(2), Now.AddHours(2)), (quote.ClosedAt!.Value, quote.UpdatedAt!.Value));
    }

    [Fact]
    public void FromItsLink_TheCustomer_ShouldRejectIt_EvenSayingNothing()
    {
        var quote = Sent();

        Assert.True(quote.ReplyFromLink(accepted: false, Today, "   ", Now));

        Assert.Equal((QuoteStatus.Rejected, QuoteReplyChannel.Link, (string?)null), (quote.Status, quote.ReplyChannel!.Value, quote.CustomerResponse));
    }

    [Fact]
    public void TheSameAnswerAgain_ShouldChangeNothing_WhileAnotherOneIsRefused()
    {
        var fromItsLink = Sent();
        fromItsLink.ReplyFromLink(accepted: true, Today, "Sí", Now);
        var recorded = Sent();
        recorded.Accept(Today, QuoteReplyChannel.Phone, "Por teléfono", Now);

        Assert.False(fromItsLink.ReplyFromLink(accepted: true, Today, "Otra vez", Now.AddHours(1)));
        Assert.False(recorded.ReplyFromLink(accepted: true, Today, null, Now.AddHours(1)));
        Assert.Equal(QuoteRules.AnsweredAlreadyMessage,
            Assert.Throws<DomainException>(() => fromItsLink.ReplyFromLink(accepted: false, Today, null, Now.AddHours(1))).Message);

        Assert.Equal(("Sí", Now, QuoteReplyChannel.Link), (fromItsLink.CustomerResponse, fromItsLink.ClosedAt!.Value, fromItsLink.ReplyChannel!.Value));
        Assert.Equal(("Por teléfono", QuoteReplyChannel.Phone), (recorded.CustomerResponse, recorded.ReplyChannel!.Value));
    }

    [Fact]
    public void AVersionReplacedOrExpired_ShouldNotBeAnswered_FromItsLink()
    {
        var replaced = Sent();
        replaced.Revise(2, Now).Send(Today, new string('b', 43), replaced, Now);
        var expired = Sent(validityDays: 1);

        Assert.Equal(QuoteRules.ReplacedMessage,
            Assert.Throws<DomainException>(() => replaced.ReplyFromLink(accepted: true, Today, null, Now)).Message);
        Assert.Equal(QuoteRules.ExpiredForCustomerMessage,
            Assert.Throws<DomainException>(() => expired.ReplyFromLink(accepted: true, Today.AddDays(2), null, Now)).Message);
        Assert.True(expired.ReplyFromLink(accepted: true, Today.AddDays(1), null, Now));
        // A draft has no link; were it asked, the customer reads a message for them, not the business's.
        var draft = Quote.Start(Business, Folios.QuoteNumber(9), Valeria, null, true, 15, Now);
        Assert.Equal(QuoteRules.NotAnswerableMessage,
            Assert.Throws<DomainException>(() => draft.ReplyFromLink(accepted: true, Today, null, Now)).Message);
    }

    [Fact]
    public void WhatTheCustomerSays_ShouldBeUpTo500Characters()
    {
        var quote = Sent();

        Assert.Equal(QuoteRules.CustomerResponseTooLongMessage,
            Assert.Throws<DomainException>(() => quote.ReplyFromLink(accepted: true, Today, new string('a', 501), Now)).Message);
        Assert.Equal(QuoteStatus.Sent, quote.Status);
        Assert.True(quote.ReplyFromLink(accepted: true, Today, new string('a', 500), Now));
    }

    [Fact]
    public void TheBusiness_ShouldNeverRecordAnAnswerFromTheLink()
    {
        var quote = Sent();

        Assert.Equal(QuoteRules.LinkChannelMessage,
            Assert.Throws<DomainException>(() => quote.Accept(Today, QuoteReplyChannel.Link, null, Now)).Message);
        Assert.Equal(QuoteRules.LinkChannelMessage,
            Assert.Throws<DomainException>(() => quote.Reject(Today, QuoteReplyChannel.Link, null, Now)).Message);
        Assert.Equal(QuoteStatus.Sent, quote.Status);
    }

    [Fact]
    public void AcceptedFromItsLink_ShouldBecomeTheJob_AndNoCorrectionBeSentAfter()
    {
        var quote = Sent();
        var correction = quote.Revise(2, Now);
        quote.ReplyFromLink(accepted: true, Today, null, Now);

        Assert.Equal(QuoteRules.NotReplaceableMessage,
            Assert.Throws<DomainException>(() => correction.Send(Today, new string('b', 43), quote, Now)).Message);
        var order = Guid.NewGuid();
        quote.ConvertToOrder(order, 0m, Now.AddHours(1));
        Assert.Equal(order, quote.OrderId);
    }

    [Fact]
    public void ABusiness_ShouldLetTheCustomerAnswerFromTheLink_UntilItTurnsItOff()
    {
        var settings = new TenantSettings(Business);

        Assert.True(settings.QuoteOnlineReplies);
        settings.SetQuoteOnlineReplies(false);
        Assert.False(settings.QuoteOnlineReplies);
    }
}
