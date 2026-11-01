using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The quote (Fase 29C, ADR 0053 points 14, 15 and 17): a draft with its lines whose taxes and total the aggregate works out,
/// sent with its validity and its link, corrected with a new version that replaces it when sent, expired once its validity
/// passed, and accepted or rejected as the business records the customer's answer.
/// </summary>
public class QuoteTests
{
    private static readonly Guid Business = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 2);
    private static readonly string Token = new('a', 43);
    private static readonly QuoteCustomer Valeria = new(null, "Valeria Ruiz", "+525522107765", "valeria@correo.mx");

    private static Quote Draft(bool pricesIncludeTax = true, int validityDays = 15, Guid? appointmentId = null)
        => Quote.Start(Business, Folios.QuoteNumber(7), Valeria, appointmentId, pricesIncludeTax, validityDays, Now);

    private static QuoteLineInput Line(string description = "Cambio de calentador", decimal quantity = 1m, decimal unitPrice = 1160m,
        VatRate? rate = null, Guid? productId = null)
        => new(productId, description, quantity, unitPrice, rate ?? VatRate.General);

    private static Quote Sent(int validityDays = 15)
    {
        var quote = Draft(validityDays: validityDays);
        quote.ReplaceLines([Line()], Now);
        quote.Send(Today, Token, null, Now);
        return quote;
    }

    [Fact]
    public void Start_ShouldBeTheFirstVersion_ADraftWithoutLines_ForItsCustomer()
    {
        var quote = Draft();

        Assert.Equal(("COT-000007", 1, QuoteStatus.Draft), (quote.Number, quote.Revision, quote.Status));
        Assert.Equal(("Valeria Ruiz", "+525522107765", "valeria@correo.mx"), (quote.CustomerName, quote.CustomerPhone, quote.CustomerEmail));
        Assert.Equal((15, (DateOnly?)null, (string?)null), (quote.ValidityDays, quote.ValidUntil, quote.TrackingToken));
        Assert.Empty(quote.Lines);
        Assert.Equal(0m, quote.Total);
    }

    [Theory]
    [InlineData("", "+525522107765", null, QuoteRules.CustomerNameMessage)]
    [InlineData("Valeria", " ", null, QuoteRules.CustomerPhoneMessage)]
    [InlineData("Valeria", "+525522107765", "valeria", InvoiceEmailAddress.InvalidMessage)]
    public void Customer_ShouldNeedANameAndAPhone_AndAnEmailThatIsOne(string name, string phone, string? email, string message)
    {
        var error = Assert.Throws<DomainException>(() => new QuoteCustomer(null, name, phone, email));
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void Customer_ShouldRefuseANameLongerThanItKeeps()
    {
        var error = Assert.Throws<DomainException>(() => new QuoteCustomer(null, new string('a', 201), "+525522107765", null));
        Assert.Equal(QuoteRules.CustomerNameTooLongMessage, error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public void Validity_ShouldGoFromOneToNinetyDays(int days)
    {
        var error = Assert.Throws<DomainException>(() => Draft(validityDays: days));
        Assert.Equal(QuoteRules.ValidityDaysMessage, error.Message);
        Assert.Throws<DomainException>(() => new TenantSettings(Business, TenantCapability.Service).SetQuoteValidityDays(days));
    }

    [Fact]
    public void TheBusiness_ShouldGiveFifteenDays_UntilTheOwnerPicksOtherwise()
    {
        var settings = new TenantSettings(Business, TenantCapability.Service);
        Assert.Equal(15, settings.QuoteValidityDays);

        settings.SetQuoteValidityDays(30);
        Assert.Equal(30, settings.QuoteValidityDays);
    }

    [Fact]
    public void Lines_ShouldGiveTheTotal_WithTheIvaInsideThePrices()
    {
        var quote = Draft(pricesIncludeTax: true);

        quote.ReplaceLines([Line(unitPrice: 1160m), Line("Mano de obra", 2.5m, 232m), Line("Tubo", 1m, 100m, VatRate.Exempt)], Now);

        // 1,160 + 580 = 1,740 with IVA inside: 1,500 + 240; the exempt 100 adds no IVA.
        Assert.Equal((1600m, 240m, 1840m), (quote.SubTotal, quote.Tax, quote.Total));
        Assert.Equal([1, 2, 3], quote.Lines.Select(line => line.Position));
        Assert.Equal(580m, quote.Lines.Single(line => line.Position == 2).Total);
    }

    [Fact]
    public void Lines_ShouldAddTheIva_WhenThePricesDoNotIncludeIt()
    {
        var quote = Draft(pricesIncludeTax: false);

        quote.ReplaceLines([Line(unitPrice: 1000m), Line("Material", 3m, 33.33m, VatRate.Of(8m))], Now);

        // 1,000 at 16 % adds 160; 99.99 at 8 % adds 8.00 (7.9992 to cents).
        Assert.Equal((1099.99m, 168m, 1267.99m), (quote.SubTotal, quote.Tax, quote.Total));
    }

    [Theory]
    [InlineData("", 1, 100, QuoteRules.DescriptionMessage)]
    [InlineData("Tubo", 0, 100, QuoteRules.QuantityMessage)]
    [InlineData("Tubo", 0.0001, 100, QuoteRules.QuantityMessage)]
    [InlineData("Tubo", 10001, 100, QuoteRules.QuantityMessage)]
    [InlineData("Tubo", 1, -1, QuoteRules.UnitPriceMessage)]
    [InlineData("Tubo", 1, 10.001, QuoteRules.UnitPriceMessage)]
    [InlineData("Tubo", 1, 1000001, QuoteRules.UnitPriceMessage)]
    public void ALine_ShouldSayWhatItIncludes_WithAQuantityAndAPriceThatFit(string description, double quantity, double price, string message)
    {
        var error = Assert.Throws<DomainException>(() => Line(description, (decimal)quantity, (decimal)price));
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void Lines_ShouldBeFiftyAtMost()
    {
        var quote = Draft();

        var error = Assert.Throws<DomainException>(() => quote.ReplaceLines(Enumerable.Range(0, 51).Select(_ => Line()).ToList(), Now));

        Assert.Equal(QuoteRules.TooManyLinesMessage, error.Message);
    }

    [Fact]
    public void Send_ShouldGiveItsValidityFromToday_AndItsLink()
    {
        var quote = Sent(validityDays: 15);

        Assert.Equal((QuoteStatus.Sent, new DateOnly(2026, 11, 17), Token, Now),
            (quote.Status, quote.ValidUntil!.Value, quote.TrackingToken, quote.SentAt!.Value));
        Assert.Equal(QuoteStatus.Sent, quote.StatusOn(new DateOnly(2026, 11, 17)));
    }

    [Fact]
    public void Send_ShouldNeedALineWithAPrice()
    {
        var empty = Draft();
        var free = Draft();
        free.ReplaceLines([Line(unitPrice: 0m)], Now);

        foreach (var quote in new[] { empty, free })
        {
            var error = Assert.Throws<DomainException>(() => quote.Send(Today, Token, null, Now));
            Assert.Equal(QuoteRules.NothingToSendMessage, error.Message);
            Assert.Equal(QuoteStatus.Draft, quote.Status);
        }
    }

    [Fact]
    public void ASentQuote_ShouldNotChange()
    {
        var quote = Sent();

        Assert.Equal(QuoteRules.NotDraftMessage, Assert.Throws<DomainException>(() => quote.ReplaceLines([Line(unitPrice: 1m)], Now)).Message);
        Assert.Equal(QuoteRules.NotDraftMessage, Assert.Throws<DomainException>(() => quote.UpdateDetails(Valeria, 30, "Otra", Now)).Message);
        Assert.Equal(QuoteRules.NotDraftMessage, Assert.Throws<DomainException>(() => quote.Send(Today, Token, null, Now)).Message);
        Assert.Equal(1160m, quote.Total);
    }

    [Fact]
    public void AQuote_ShouldReadExpired_TheDayAfterItsValidity_AndItsLinkOpensThirtyDaysMore()
    {
        var quote = Sent(validityDays: 15);

        Assert.Equal(QuoteStatus.Expired, quote.StatusOn(new DateOnly(2026, 11, 18)));
        // Stored as sent: nothing runs to expire it.
        Assert.Equal(QuoteStatus.Sent, quote.Status);
        Assert.True(quote.LinkOpensOn(new DateOnly(2026, 12, 17)));
        Assert.False(quote.LinkOpensOn(new DateOnly(2026, 12, 18)));
        Assert.False(Draft().LinkOpensOn(Today));
    }

    [Fact]
    public void Accept_ShouldKeepWhatTheCustomerAnswered_AndHow()
    {
        var quote = Sent();

        quote.Accept(Today, QuoteReplyChannel.WhatsApp, "  Sí, el jueves por la mañana  ", Now.AddHours(1));

        Assert.Equal((QuoteStatus.Accepted, QuoteReplyChannel.WhatsApp, "Sí, el jueves por la mañana", Now.AddHours(1)),
            (quote.Status, quote.ReplyChannel!.Value, quote.CustomerResponse, quote.ClosedAt!.Value));
        // Accepted is final: it is not answered again nor corrected.
        Assert.Throws<DomainException>(() => quote.Reject(Today, QuoteReplyChannel.Phone, null, Now));
        Assert.Equal(QuoteRules.CannotReviseMessage, Assert.Throws<DomainException>(() => quote.Revise(2, Now)).Message);
    }

    [Fact]
    public void Reject_ShouldKeepWhatTheCustomerAnswered_EvenNothing()
    {
        var quote = Sent();

        quote.Reject(Today, QuoteReplyChannel.Phone, " ", Now);

        Assert.Equal((QuoteStatus.Rejected, QuoteReplyChannel.Phone, (string?)null), (quote.Status, quote.ReplyChannel!.Value, quote.CustomerResponse));
    }

    [Fact]
    public void TheAnswer_ShouldBeRecordedOnlyOnAValidSentQuote()
    {
        Assert.Equal(QuoteRules.NotSentMessage,
            Assert.Throws<DomainException>(() => Draft().Accept(Today, QuoteReplyChannel.WhatsApp, null, Now)).Message);

        var expired = Sent(validityDays: 1);
        Assert.Equal(QuoteRules.ExpiredMessage,
            Assert.Throws<DomainException>(() => expired.Accept(Today.AddDays(2), QuoteReplyChannel.WhatsApp, null, Now)).Message);
        Assert.Equal(QuoteRules.ExpiredMessage,
            Assert.Throws<DomainException>(() => expired.Reject(Today.AddDays(2), QuoteReplyChannel.WhatsApp, null, Now)).Message);

        var quote = Sent();
        Assert.Equal(QuoteRules.ResponseTooLongMessage, Assert.Throws<DomainException>(
            () => quote.Accept(Today, QuoteReplyChannel.Email, new string('a', 501), Now)).Message);
        Assert.Equal(QuoteRules.ReplyChannelMessage, Assert.Throws<DomainException>(
            () => quote.Accept(Today, (QuoteReplyChannel)9, null, Now)).Message);
        Assert.Equal(QuoteStatus.Sent, quote.Status);
    }

    [Fact]
    public void Revise_ShouldStartTheNextVersion_WithWhatItSays_AndLeaveThisOneUntilItIsSent()
    {
        var appointment = Guid.NewGuid();
        var quote = Draft(appointmentId: appointment);
        quote.UpdateDetails(Valeria, 20, "Incluye material", Now);
        quote.ReplaceLines([Line(), Line("Mano de obra", 2m, 300m)], Now);
        quote.SetVisitCredit(new QuoteVisitCredit(Guid.NewGuid(), 350m), Now);
        quote.Send(Today, Token, null, Now);

        var revision = quote.Revise(2, Now.AddDays(1));

        Assert.Equal(("COT-000007", 2, QuoteStatus.Draft), (revision.Number, revision.Revision, revision.Status));
        Assert.Equal((appointment, 20, "Incluye material", 350m, true),
            (revision.AppointmentId!.Value, revision.ValidityDays, revision.Note, revision.VisitCreditAmount!.Value, revision.PricesIncludeTax));
        Assert.Equal(quote.Lines.Select(line => (line.Position, line.Description, line.Total)),
            revision.Lines.Select(line => (line.Position, line.Description, line.Total)));
        Assert.Equal(quote.Total, revision.Total);
        Assert.Equal(((DateOnly?)null, (string?)null), (revision.ValidUntil, revision.TrackingToken));
        Assert.Equal(QuoteStatus.Sent, quote.Status);
    }

    [Fact]
    public void SendingACorrection_ShouldReplaceTheVersionTheCustomerHad_WhichKeepsWhatItSaid()
    {
        var quote = Sent();
        var revision = quote.Revise(2, Now);
        revision.ReplaceLines([Line(unitPrice: 990m)], Now);

        revision.Send(Today.AddDays(3), new string('b', 43), quote, Now.AddDays(3));

        Assert.Equal((QuoteStatus.Superseded, 1160m, Token, Now.AddDays(3)), (quote.Status, quote.Total, quote.TrackingToken, quote.ClosedAt!.Value));
        Assert.Equal((QuoteStatus.Sent, 990m, new DateOnly(2026, 11, 20)), (revision.Status, revision.Total, revision.ValidUntil!.Value));
    }

    [Fact]
    public void ARejectedQuoteReplaced_ShouldKeepWhenTheCustomerAnswered()
    {
        var rejected = Sent();
        rejected.Reject(Today, QuoteReplyChannel.WhatsApp, "Está caro", Now.AddDays(1));
        var counter = rejected.Revise(2, Now.AddDays(10));

        counter.Send(Today.AddDays(10), new string('b', 43), rejected, Now.AddDays(10));

        Assert.Equal((QuoteStatus.Superseded, Now.AddDays(1), "Está caro"), (rejected.Status, rejected.ClosedAt!.Value, rejected.CustomerResponse));
    }

    [Fact]
    public void Revising_ShouldTouchTheVersionTheCustomerHas_SoAChangeOfItAtTheSameTimeCollides()
    {
        var quote = Sent();

        quote.Revise(2, Now.AddDays(2));

        Assert.Equal((QuoteStatus.Sent, Now.AddDays(2)), (quote.Status, quote.UpdatedAt!.Value));
    }

    [Fact]
    public void ARejectedOrExpiredQuote_ShouldBeCorrected()
    {
        var rejected = Sent();
        rejected.Reject(Today, QuoteReplyChannel.WhatsApp, "Está caro", Now);
        var counter = rejected.Revise(2, Now);
        counter.Send(Today, new string('b', 43), rejected, Now);
        Assert.Equal(QuoteStatus.Superseded, rejected.Status);

        var expired = Sent(validityDays: 1);
        var renewed = expired.Revise(2, Now.AddDays(5));
        renewed.Send(Today.AddDays(5), new string('c', 43), expired, Now.AddDays(5));
        Assert.Equal((QuoteStatus.Superseded, QuoteStatus.Sent), (expired.Status, renewed.StatusOn(Today.AddDays(5))));
    }

    [Fact]
    public void ACorrection_ShouldNotBeSent_OnceTheCustomerAcceptedTheVersionTheyHad()
    {
        var quote = Sent();
        var revision = quote.Revise(2, Now);
        quote.Accept(Today, QuoteReplyChannel.WhatsApp, null, Now);

        var error = Assert.Throws<DomainException>(() => revision.Send(Today, new string('b', 43), quote, Now));

        Assert.Equal(QuoteRules.NotReplaceableMessage, error.Message);
        Assert.Equal((QuoteStatus.Accepted, QuoteStatus.Draft), (quote.Status, revision.Status));
    }

    [Fact]
    public void ACorrection_ShouldReplaceAnEarlierVersionOfTheSameQuote_AndAFirstVersionNone()
    {
        var quote = Sent();
        var revision = quote.Revise(2, Now);
        var other = Quote.Start(Business, Folios.QuoteNumber(8), Valeria, null, true, 15, Now);
        other.ReplaceLines([Line()], Now);
        other.Send(Today, new string('d', 43), null, Now);

        Assert.Throws<ArgumentException>(() => revision.Send(Today, new string('b', 43), null, Now));
        Assert.Throws<ArgumentException>(() => revision.Send(Today, new string('b', 43), other, Now));
        var first = Draft();
        first.ReplaceLines([Line()], Now);
        Assert.Throws<ArgumentException>(() => first.Send(Today, new string('b', 43), quote, Now));
        Assert.Throws<ArgumentException>(() => first.Send(Today, "corto", null, Now));
    }

    [Fact]
    public void ACorrection_ShouldComeAfterEveryVersion_TheDiscardedDraftsIncluded()
    {
        var quote = Sent();
        quote.Revise(2, Now).Discard(Now);

        Assert.Equal(3, quote.Revise(3, Now).Revision);
        Assert.Throws<ArgumentOutOfRangeException>(() => quote.Revise(1, Now));
    }

    [Fact]
    public void OnlyTheVersionTheCustomerHas_ShouldBeCorrected()
    {
        var quote = Sent();
        var revision = quote.Revise(2, Now);
        revision.Send(Today, new string('b', 43), quote, Now);

        Assert.Equal(QuoteRules.CannotReviseMessage, Assert.Throws<DomainException>(() => quote.Revise(2, Now)).Message);
        Assert.Equal(QuoteRules.CannotReviseMessage, Assert.Throws<DomainException>(() => Draft().Revise(2, Now)).Message);
    }

    [Fact]
    public void Discard_ShouldDropOnlyADraft_AndKeepIt()
    {
        var draft = Draft();
        draft.Discard(Now);
        Assert.Equal((QuoteStatus.Discarded, Now), (draft.Status, draft.ClosedAt!.Value));

        var error = Assert.Throws<DomainException>(() => Sent().Discard(Now));
        Assert.Equal(QuoteRules.DraftOnlyDiscardMessage, error.Message);
    }

    [Fact]
    public void TheVisitCredit_ShouldComeOffWhileItsSaleIsNotCancelled_NeverBelowZero()
    {
        var quote = Draft(appointmentId: Guid.NewGuid());
        quote.ReplaceLines([Line(unitPrice: 1160m)], Now);
        quote.SetVisitCredit(new QuoteVisitCredit(Guid.NewGuid(), 350m), Now);

        Assert.Equal((810m, 1160m), (quote.ToPay(visitCreditApplies: true), quote.ToPay(visitCreditApplies: false)));

        quote.ReplaceLines([Line(unitPrice: 200m)], Now);
        Assert.Equal(0m, quote.ToPay(visitCreditApplies: true));

        quote.SetVisitCredit(null, Now);
        Assert.Equal((200m, (decimal?)null), (quote.ToPay(visitCreditApplies: true), quote.VisitCreditAmount));
    }

    [Fact]
    public void TheVisitCredit_ShouldComeOnlyFromAVisit_ForWhatWasPaid()
    {
        var error = Assert.Throws<DomainException>(() => Draft().SetVisitCredit(new QuoteVisitCredit(Guid.NewGuid(), 350m), Now));
        Assert.Equal(QuoteRules.VisitCreditMessage, error.Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuoteVisitCredit(Guid.NewGuid(), 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuoteVisitCredit(Guid.NewGuid(), 10.001m));
        Assert.Throws<ArgumentException>(() => new QuoteVisitCredit(Guid.Empty, 10m));
    }

    [Fact]
    public void TheNote_ShouldFitInAThousandCharacters()
    {
        var quote = Draft();

        var error = Assert.Throws<DomainException>(() => quote.UpdateDetails(Valeria, 15, new string('a', 1001), Now));

        Assert.Equal(QuoteRules.NoteTooLongMessage, error.Message);
        quote.UpdateDetails(Valeria, 15, "   ", Now);
        Assert.Null(quote.Note);
    }

    [Fact]
    public void Folios_ShouldCountQuotesWithoutEnd()
    {
        Assert.Equal(("COT-000123", "COT-1234567"), (Folios.QuoteNumber(123), Folios.QuoteNumber(1234567)));
    }
}
