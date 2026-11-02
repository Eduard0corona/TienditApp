using FoodParkDigital.Application.Attention;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Quotes;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using static FoodParkDigital.Application.Tests.QuoteFlowTests;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The customer answers the quote from its link (Fase 29G, ADR 0061): accepts or rejects the version the link names, while it
/// is sent and valid and the business lets them, with what they want to say; the business sees it with its channel, the
/// activity keeps that the customer answered there and nothing of who they are, and «Requiere atención» tells the business.
/// </summary>
public class QuoteLinkReplyFlowTests
{
    [Fact]
    public async Task FromItsLink_TheCustomer_ShouldAcceptIt_AndTheBusinessSeeHowTheyAnswered()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture);
        var sent = await SentAsync(fixture);
        // Another business is in context: the link carries its own.
        await TradeAsync(fixture, slug: "otro");
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddHours(3);

        var replied = await ReplyAsync(fixture, sent.TrackingToken!, accepted: true, "  Los espero el jueves  ");

        Assert.True(replied!.IsSuccess, replied.Error);
        var view = replied.Value!;
        Assert.Equal((QuoteStatus.Accepted, true, "Los espero el jueves", false),
            (view.Status, view.RepliedFromLink, view.Response, view.CanReply));
        Assert.Equal(SalesTestFixture.Now.AddHours(3), view.RepliedAt);
        fixture.TenantService.SetTenant(tenant.Id);
        var seen = await GetAsync(fixture, sent.Id);
        Assert.Equal((QuoteStatus.Accepted, QuoteReplyChannel.Link, "Los espero el jueves"),
            (seen.Status, seen.ReplyChannel!.Value, seen.CustomerResponse));
        // The activity keeps that the customer answered from the link, never what they wrote nor who they are.
        var entry = await fixture.Context.AuditLogs.IgnoreQueryFilters().SingleAsync(a => a.Action == "QuoteAccepted");
        Assert.Equal((tenant.Id, "Cliente (enlace)"), (entry.TenantId, entry.PerformedBy));
        Assert.Contains("Link", entry.Metadata);
        Assert.DoesNotContain("jueves", entry.Metadata);
        Assert.DoesNotContain("5522107765", entry.Metadata);
    }

    [Fact]
    public async Task FromItsLink_TheCustomer_ShouldRejectIt_EvenSayingNothing()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture);
        var sent = await SentAsync(fixture);

        var replied = await ReplyAsync(fixture, sent.TrackingToken!, accepted: false, null);

        Assert.Equal((QuoteStatus.Rejected, true, (string?)null), (replied!.Value!.Status, replied.Value.RepliedFromLink, replied.Value.Response));
        Assert.Equal(QuoteReplyChannel.Link, (await GetAsync(fixture, sent.Id)).ReplyChannel);
        Assert.Equal(1, await fixture.Context.AuditLogs.CountAsync(a => a.Action == "QuoteRejected" && a.PerformedBy == "Cliente (enlace)"));
    }

    [Fact]
    public async Task TheSameAnswerAgain_ShouldChangeNothing_WhileAnotherOneIsRefused()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture);
        var sent = await SentAsync(fixture);
        await ReplyAsync(fixture, sent.TrackingToken!, accepted: true, "Sí");
        var recorded = await SentAsync(fixture);
        await Handler(fixture).Handle(new RecordQuoteReplyCommand(recorded.Id, true, QuoteReplyChannel.Phone, "Por teléfono"), CancellationToken.None);

        var again = await ReplyAsync(fixture, sent.TrackingToken!, accepted: true, "Otra vez");
        var other = await ReplyAsync(fixture, sent.TrackingToken!, accepted: false, null);
        var alreadyRecorded = await ReplyAsync(fixture, recorded.TrackingToken!, accepted: true, null);

        Assert.Equal((true, "Sí"), (again!.IsSuccess, again.Value!.Response));
        Assert.Equal(QuoteRules.AnsweredAlreadyMessage, other!.Error);
        // What the business recorded stays theirs: the link says it was accepted, not what the business wrote down.
        Assert.Equal((QuoteStatus.Accepted, false, (string?)null),
            (alreadyRecorded!.Value!.Status, alreadyRecorded.Value.RepliedFromLink, alreadyRecorded.Value.Response));
        Assert.Equal(1, await fixture.Context.AuditLogs.CountAsync(a => a.Action == "QuoteAccepted" && a.PerformedBy == "Cliente (enlace)"));
        Assert.Equal(QuoteReplyChannel.Phone, (await GetAsync(fixture, recorded.Id)).ReplyChannel);
    }

    [Fact]
    public async Task AcceptingFromItsLink_ShouldDiscardACorrectionStartedOnIt_WhichCouldNeverBeSent()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture);
        var sent = await SentAsync(fixture);
        var correction = (await Handler(fixture).Handle(new ReviseQuoteCommand(sent.Id), CancellationToken.None)).Value!;

        await ReplyAsync(fixture, sent.TrackingToken!, accepted: true, null);

        Assert.Equal(QuoteStatus.Discarded, (await GetAsync(fixture, correction.Id)).Status);
        Assert.Equal(1, await fixture.Context.AuditLogs.CountAsync(a => a.Action == "QuoteDiscarded" && a.PerformedBy == "Cliente (enlace)"));
    }

    [Fact]
    public async Task AVersionReplacedOrExpired_ShouldNotBeAnswered_AndALinkPastItsDaysNotFound()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture);
        var sent = await SentAsync(fixture);
        var correction = (await Handler(fixture).Handle(new ReviseQuoteCommand(sent.Id), CancellationToken.None)).Value!;
        var resent = (await Handler(fixture).Handle(new SendQuoteCommand(correction.Id), CancellationToken.None)).Value!;

        Assert.Equal(QuoteRules.ReplacedMessage, (await ReplyAsync(fixture, sent.TrackingToken!, accepted: true, null))!.Error);
        Assert.False((await PublicAsync(fixture, sent.TrackingToken!))!.CanReply);
        Assert.True((await PublicAsync(fixture, resent.TrackingToken!))!.CanReply);

        // 20 October, the day after its validity, in Mexico City.
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddDays(16);
        Assert.Equal(QuoteRules.ExpiredForCustomerMessage, (await ReplyAsync(fixture, resent.TrackingToken!, accepted: true, null))!.Error);
        Assert.False((await PublicAsync(fixture, resent.TrackingToken!))!.CanReply);
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddDays(46);
        Assert.Null(await ReplyAsync(fixture, resent.TrackingToken!, accepted: true, null));
        Assert.Null(await ReplyAsync(fixture, resent.TrackingToken!.ToUpperInvariant(), accepted: true, null));
        Assert.Null(await ReplyAsync(fixture, "corto", accepted: true, null));
        Assert.Equal(QuoteStatus.Sent, (await fixture.Context.Quotes.AsNoTracking().SingleAsync(q => q.Id == resent.Id)).Status);
    }

    [Fact]
    public async Task ABusinessThatTurnsItOff_ShouldRecordTheAnswerItself_AndTurningItOnAgainLetTheCustomerAnswer()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture);
        var sent = await SentAsync(fixture);
        Assert.True((await Handler(fixture).Handle(new GetQuoteSettingsQuery(), CancellationToken.None)).OnlineReplies);

        var off = await Handler(fixture).Handle(new UpdateQuoteSettingsCommand(15, OnlineReplies: false), CancellationToken.None);
        var kept = await Handler(fixture).Handle(new UpdateQuoteSettingsCommand(20), CancellationToken.None);

        Assert.Equal((false, false, 20), (off.Value!.OnlineReplies, kept.Value!.OnlineReplies, kept.Value.ValidityDays));
        Assert.False((await PublicAsync(fixture, sent.TrackingToken!))!.CanReply);
        Assert.Equal(QuoteRules.OnlineRepliesOffMessage, (await ReplyAsync(fixture, sent.TrackingToken!, accepted: true, null))!.Error);
        Assert.Equal(QuoteStatus.Sent, (await GetAsync(fixture, sent.Id)).Status);
        var entries = await fixture.Context.AuditLogs.Where(a => a.Action == "QuoteSettingsUpdated").ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Metadata!.Contains("\"OnlineReplies\":false") && e.Metadata.Contains("\"PreviousOnlineReplies\":true"));

        await Handler(fixture).Handle(new UpdateQuoteSettingsCommand(20, OnlineReplies: true), CancellationToken.None);
        Assert.True((await ReplyAsync(fixture, sent.TrackingToken!, accepted: true, null))!.IsSuccess);
    }

    [Fact]
    public async Task AnAnswerTheBusinessRecordsAtTheSameTime_ShouldWin_AndNothingOfTheCustomersStay()
    {
        var meanwhile = new BusinessAnswersMeanwhile();
        await using var fixture = SalesTestFixture.Create(interceptors: meanwhile);
        await TradeAsync(fixture);
        var sent = await SentAsync(fixture);
        meanwhile.Tenant = fixture.TenantService;
        meanwhile.Armed = true;

        var refused = await ReplyAsync(fixture, sent.TrackingToken!, accepted: true, "Los espero");

        Assert.Equal(QuoteRules.ChangedMeanwhileMessage, refused!.Error);
        await using var fresh = new SalesDbContext(meanwhile.Options!, fixture.TenantService);
        var saved = await fresh.Quotes.AsNoTracking().SingleAsync(q => q.Id == sent.Id);
        Assert.Equal((QuoteStatus.Rejected, QuoteReplyChannel.Phone), (saved.Status, saved.ReplyChannel!.Value));
        Assert.Equal(0, await fresh.AuditLogs.CountAsync(a => a.PerformedBy == "Cliente (enlace)"));
    }

    [Fact]
    public async Task RequiereAtencion_ShouldCountTheAcceptedQuotesToConvert_ThoseFromTheLinkUrgent()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture);
        fixture.CurrentUser.TenantRole = "Staff";
        fixture.CurrentUser.Permissions = ["ManageOrders"];
        var fromLink = await SentAsync(fixture);
        var recorded = await SentAsync(fixture);
        var converted = await SentAsync(fixture);
        var waiting = await SentAsync(fixture);
        await ReplyAsync(fixture, fromLink.TrackingToken!, accepted: true, null);
        await Handler(fixture).Handle(new RecordQuoteReplyCommand(recorded.Id, true, QuoteReplyChannel.WhatsApp, null), CancellationToken.None);
        await Handler(fixture).Handle(new RecordQuoteReplyCommand(converted.Id, true, QuoteReplyChannel.WhatsApp, null), CancellationToken.None);
        var quote = await fixture.Context.Quotes.SingleAsync(q => q.Id == converted.Id);
        quote.ConvertToOrder(Guid.NewGuid(), 0m, SalesTestFixture.Now);
        await fixture.Context.SaveChangesAsync();

        var item = Assert.Single((await AttentionAsync(fixture)).Items, i => i.Key == AttentionKeys.QuotesToConvert);
        Assert.Equal((2, 1), (item.Count, item.UrgentCount));
        Assert.Equal(QuoteStatus.Sent, (await GetAsync(fixture, waiting.Id)).Status);

        // After 30 days they no longer wait there; nor for whoever does not take orders.
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddDays(GetAttentionQueryHandler.QuoteToConvertDays + 1);
        Assert.DoesNotContain((await AttentionAsync(fixture)).Items, i => i.Key == AttentionKeys.QuotesToConvert && i.Count > 0);
        fixture.Clock.UtcNow = SalesTestFixture.Now;
        // A member of the team who only handles the agenda (an owner or an admin takes orders by being one).
        fixture.CurrentUser.TenantRole = "Staff";
        fixture.CurrentUser.Permissions = ["ManageAppointments"];
        Assert.DoesNotContain((await AttentionAsync(fixture)).Items, i => i.Key == AttentionKeys.QuotesToConvert);
    }

    [Fact]
    public void Validators_ShouldRefuseTheLinkForTheBusiness_AndALongMessageForTheCustomer()
    {
        var recorded = new RecordQuoteReplyCommandValidator().Validate(
            new RecordQuoteReplyCommand(Guid.NewGuid(), true, QuoteReplyChannel.Link, null));
        var tooLong = new ReplyToQuoteFromLinkCommandValidator().Validate(
            new ReplyToQuoteFromLinkCommand(new string('a', 43), true, new string('a', 501)));
        var noLink = new ReplyToQuoteFromLinkCommandValidator().Validate(new ReplyToQuoteFromLinkCommand("", true, null));

        Assert.Equal([QuoteRules.LinkChannelMessage], recorded.Errors.Select(e => e.ErrorMessage));
        Assert.Equal([QuoteRules.CustomerResponseTooLongMessage], tooLong.Errors.Select(e => e.ErrorMessage));
        Assert.False(noLink.IsValid);
    }

    private static Task<Common.Models.Result<PublicQuoteDto>?> ReplyAsync(SalesTestFixture fixture, string token, bool accepted, string? response)
        => new ReplyToQuoteFromLinkCommandHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new ReplyToQuoteFromLinkCommand(token, accepted, response), CancellationToken.None);

    private static Task<AttentionDto> AttentionAsync(SalesTestFixture fixture)
        => new GetAttentionQueryHandler(
                fixture.Context, TestAccess.For(fixture.CurrentUser, fixture.TenantService), fixture.CurrentUser, fixture.Clock)
            .Handle(new GetAttentionQuery(), CancellationToken.None);

    /// <summary>
    /// The save of the customer's answer finds that the business (with its own context) recorded another one first: the
    /// database refuses this one as stale.
    /// </summary>
    private sealed class BusinessAnswersMeanwhile : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public ICurrentTenantService? Tenant { get; set; }
        public DbContextOptions<ApplicationDbContext>? Options { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var answering = eventData.Context!.ChangeTracker.Entries<Quote>()
                .FirstOrDefault(entry => entry.State == EntityState.Modified && entry.Entity.ReplyChannel == QuoteReplyChannel.Link)?.Entity;
            if (!Armed || answering is null)
                return result;
            Armed = false;

            Options = (DbContextOptions<ApplicationDbContext>)eventData.Context.GetService<IDbContextOptions>();
            await using var other = new SalesDbContext(Options, Tenant!);
            var theirs = await other.Quotes.SingleAsync(q => q.Id == answering.Id, cancellationToken);
            theirs.Reject(DateOnly.FromDateTime(SalesTestFixture.Now.UtcDateTime), QuoteReplyChannel.Phone, null, SalesTestFixture.Now);
            await other.SaveChangesAsync(cancellationToken);
            throw new DbUpdateConcurrencyException("The business answered meanwhile.");
        }
    }
}
