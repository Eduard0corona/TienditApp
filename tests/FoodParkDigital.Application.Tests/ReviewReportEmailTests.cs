using FoodParkDigital.Application.Reviews.Notices;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Reviews.Entities;
using FoodParkDigital.Domain.Reviews.Enums;
using FoodParkDigital.Domain.Reviews.Events;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static FoodParkDigital.Application.Tests.Support.NoticeEmailsSupport;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The email of a reported review (Fase 32F, ADR 0098): one per admin of the platform, what it says and never says, when it is
/// no longer sent, and the business that reports many at once.
/// </summary>
public class ReviewReportEmailTests
{
    [Fact]
    public async Task TheEmails_ShouldGoToThePlatformAdmins_WhoseAccountIsActiveAndConfirmed_OnePerAdmin()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture, "cafe", "Café Luna");
        var first = await PlatformAdminAsync(fixture, "ana");
        var second = await PlatformAdminAsync(fixture, "beto");
        // An admin whose account was disabled, one who never confirmed the email, and the business's own owner.
        await PlatformAdminAsync(fixture, "antes", active: false);
        await PlatformAdminAsync(fixture, "nuevo", emailConfirmed: false);
        await MemberAsync(fixture, shop, "dueña", TenantUserRole.Owner);
        var review = await ReportedAsync(fixture, shop, 1, "Pésimo.", ReviewConcern.Offensive);

        var outbox = await QueueAsync(fixture, shop, review);

        Assert.Equal(new[] { first, second }.Order(), outbox.Added.Select(q => q.Event.UserId).Order());
        Assert.All(outbox.Added, q => Assert.Equal((review.Id, (Guid?)shop.Id), (q.Event.ReviewId, q.TenantId)));
    }

    [Fact]
    public async Task NoneShouldBeQueued_OnceTheReportIsClosed_OrTheReviewIsGone()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture, "cafe", "Café Luna");
        await PlatformAdminAsync(fixture, "ana");
        var kept = await ReportedAsync(fixture, shop, 2, "Tardó.", ReviewConcern.Other);
        var hidden = await ReportedAsync(fixture, shop, 1, "Pésimo.", ReviewConcern.Offensive);
        var deleted = await ReportedAsync(fixture, shop, 1, "Malo.", ReviewConcern.Spam);
        await ChangeAsync(fixture, kept, r => r.Keep(SalesTestFixture.Now));
        await ChangeAsync(fixture, hidden, r => r.Hide(ReviewConcern.Offensive, SalesTestFixture.Now));
        fixture.Context.BusinessReviews.Remove(await fixture.Context.BusinessReviews.IgnoreQueryFilters().SingleAsync(r => r.Id == deleted.Id));
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        foreach (var review in new[] { kept, hidden, deleted })
            Assert.Empty((await QueueAsync(fixture, shop, review)).Added);
    }

    [Fact]
    public async Task ABusinessThatReportsManyAtOnce_ShouldEmailTheFirstFive_WithinTheHour_AndTheRestWaitInTheConsole()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture, "cafe", "Café Luna");
        var other = await ShopAsync(fixture, "otra", "Café Otra");
        await PlatformAdminAsync(fixture, "ana");
        var reports = new List<BusinessReview>();
        for (var i = 0; i < ReviewReportEmail.MaxPerBusinessPerHour + 1; i++)
            reports.Add(await ReportedAsync(fixture, shop, 1, $"Reseña {i}.", ReviewConcern.Spam, at: TimeSpan.FromMinutes(i)));
        // Another business's reports never count against this one.
        for (var i = 0; i < ReviewReportEmail.MaxPerBusinessPerHour; i++)
            await ReportedAsync(fixture, other, 1, $"Otra {i}.", ReviewConcern.Spam, at: TimeSpan.FromMinutes(i));

        foreach (var review in reports.Take(ReviewReportEmail.MaxPerBusinessPerHour))
            Assert.Single((await QueueAsync(fixture, shop, review)).Added);
        Assert.Empty((await QueueAsync(fixture, shop, reports[^1])).Added);

        // Over an hour after the first two, they no longer count: the next one is emailed.
        var later = await ReportedAsync(fixture, shop, 1, "Después.", ReviewConcern.Spam, at: TimeSpan.FromMinutes(61));
        Assert.Single((await QueueAsync(fixture, shop, later)).Added);
    }

    [Fact]
    public async Task TheEmail_ShouldSayTheBusinessWhyTheStarsAndWhatItSays_Encoded_WithTheLinkToTheConsole()
    {
        await using var fixture = SalesTestFixture.Create();
        // WebUtility encodes the accents too: the names here have none, so what is checked reads plainly.
        var shop = await ShopAsync(fixture, "cafe", "Cocina <Luna>");
        var admin = await PlatformAdminAsync(fixture, "ana");
        var review = await ReportedAsync(
            fixture, shop, 2, "El <b>pan</b> llego frio.\nLlamen al 55.", ReviewConcern.PersonalData, author: "<i>Mariana</i> O.");

        var (to, subject, body) = Assert.Single(await SendAsync(fixture, shop, review, admin));

        Assert.Equal("ana@plataforma.test", to);
        Assert.Equal("Reseña reportada en Cocina <Luna>: Datos personales de alguien", subject);
        Assert.Contains(
            "Cocina &lt;Luna&gt; reportó la reseña de <strong>&lt;i&gt;Mariana&lt;/i&gt; O.</strong> (2 estrellas de 5) por: <strong>Datos personales de alguien</strong>.",
            body);
        Assert.Contains("El &lt;b&gt;pan&lt;/b&gt; llego frio. Llamen al 55.</blockquote>", body);
        Assert.Contains("Sigue publicada mientras la revisas", body);
        Assert.Contains($"<a href='{AppUrl}/platform/reviews'>Revisar en Plataforma › Reseñas</a>", body);
        Assert.Contains("Te llega porque administras la plataforma de TienditApp.", body);
        Assert.DoesNotContain("<b>", body);
        // Never the customer's account or email, and no way to stop them that does not exist.
        Assert.DoesNotContain("@", body);
        Assert.DoesNotContain("/profile", body);

        // Stars alone, one star; without the app's address, no links.
        var alone = await ReportedAsync(fixture, shop, 1, null, ReviewConcern.Other);
        var (_, aloneSubject, aloneBody) = Assert.Single(await SendAsync(fixture, shop, alone, admin, appUrl: null));
        Assert.Equal("Reseña reportada en Cocina <Luna>: Otro motivo", aloneSubject);
        Assert.Contains("(1 estrella de 5)", aloneBody);
        Assert.Contains("No escribió un comentario.", aloneBody);
        Assert.Contains("Revísala en Plataforma › Reseñas de TienditApp.", aloneBody);
        Assert.DoesNotContain("href", aloneBody);
    }

    [Theory]
    [InlineData(ReviewConcern.Offensive, "Insultos, amenazas u odio")]
    [InlineData(ReviewConcern.PersonalData, "Datos personales de alguien")]
    [InlineData(ReviewConcern.Spam, "Publicidad o nada que ver con el negocio")]
    [InlineData(ReviewConcern.NotAnExperience, "No habla de un pedido o una cita")]
    [InlineData(ReviewConcern.Other, "Otro motivo")]
    public void TheReason_ShouldReadAsTheConsoleSaysIt(ReviewConcern reason, string expected)
        => Assert.Equal(expected, ReviewReportEmail.Label(reason));

    [Fact]
    public async Task TheEmail_ShouldNotBeSent_OnceTheReportIsClosed_ADayLater_OrToWhoIsNoLongerAnAdmin()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture, "cafe", "Café Luna");
        var admin = await PlatformAdminAsync(fixture, "ana");
        var demoted = await PlatformAdminAsync(fixture, "beto");
        var kept = await ReportedAsync(fixture, shop, 2, "Tardó.", ReviewConcern.Other);
        var hidden = await ReportedAsync(fixture, shop, 1, "Pésimo.", ReviewConcern.Offensive);
        var review = await ReportedAsync(fixture, shop, 1, "Malo.", ReviewConcern.Spam);
        await ChangeAsync(fixture, kept, r => r.Keep(SalesTestFixture.Now));
        await ChangeAsync(fixture, hidden, r => r.Hide(ReviewConcern.Offensive, SalesTestFixture.Now));

        Assert.Empty(await SendAsync(fixture, shop, kept, admin));
        Assert.Empty(await SendAsync(fixture, shop, hidden, admin));
        Assert.Single(await SendAsync(fixture, shop, review, admin, after: TimeSpan.FromDays(1)));
        Assert.Empty(await SendAsync(fixture, shop, review, admin, after: TimeSpan.FromDays(1).Add(TimeSpan.FromSeconds(1))));

        // No longer the platform's admin since it was queued, or the account was disabled.
        var account = await fixture.Context.Users.SingleAsync(u => u.Id == demoted);
        account.Role = UserRole.Customer;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        Assert.Empty(await SendAsync(fixture, shop, review, demoted));
        account = await fixture.Context.Users.SingleAsync(u => u.Id == admin);
        account.IsActive = false;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        Assert.Empty(await SendAsync(fixture, shop, review, admin));
    }

    [Fact]
    public async Task AFailingProvider_ShouldThrow_SoTheOutboxRetriesThatEmailAlone()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture, "cafe", "Café Luna");
        var admin = await PlatformAdminAsync(fixture, "ana");
        var review = await ReportedAsync(fixture, shop, 1, "Malo.", ReviewConcern.Spam);

        await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(fixture, shop, review, admin, failing: true));
    }

    private static async Task<Tenant> ShopAsync(SalesTestFixture fixture, string slug, string name)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true, slug: slug);
        tenant.Update(name, $"{slug}@demo.com", null, null);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return tenant;
    }

    /// <summary>An admin of the platform: an account with no business of its own.</summary>
    private static async Task<Guid> PlatformAdminAsync(SalesTestFixture fixture, string name, bool active = true, bool emailConfirmed = true)
    {
        var email = $"{name}@plataforma.test";
        var account = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = emailConfirmed,
            FirstName = name,
            LastName = "Plataforma",
            Role = UserRole.SuperAdmin,
            IsActive = active
        };
        fixture.Context.Users.Add(account);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return account.Id;
    }

    /// <summary>A review of the business, written at the fixture's now and reported <paramref name="at"/> later (a minute by default).</summary>
    private static async Task<BusinessReview> ReportedAsync(
        SalesTestFixture fixture, Tenant business, int rating, string? comment, ReviewConcern reason, TimeSpan? at = null, string author = "Mariana O.")
    {
        var review = BusinessReview.Write(business.Id, Guid.NewGuid(), author, rating, comment, SalesTestFixture.Now);
        review.Report(reason, SalesTestFixture.Now + (at ?? TimeSpan.FromMinutes(1)));
        fixture.TenantService.SetTenant(business.Id);
        fixture.Context.BusinessReviews.Add(review);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return review;
    }

    private static async Task ChangeAsync(SalesTestFixture fixture, BusinessReview review, Action<BusinessReview> change)
    {
        change(await fixture.Context.BusinessReviews.IgnoreQueryFilters().SingleAsync(r => r.Id == review.Id));
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
    }

    private static async Task<Outbox<BusinessReviewReportEmailEvent>> QueueAsync(SalesTestFixture fixture, Tenant tenant, BusinessReview review)
    {
        fixture.TenantService.SetTenant(tenant.Id);
        var outbox = new Outbox<BusinessReviewReportEmailEvent>();
        await new BusinessReviewReportedEmailsHandler(fixture.Context, outbox, NullLogger<BusinessReviewReportedEmailsHandler>.Instance)
            .HandleAsync(new BusinessReviewReportedEvent(review.Id, tenant.Id), Context(tenant), CancellationToken.None);
        return outbox;
    }

    /// <summary>Sends one admin's email, <paramref name="after"/> the review was reported (a minute by default).</summary>
    private static async Task<List<(string To, string Subject, string Body)>> SendAsync(
        SalesTestFixture fixture, Tenant tenant, BusinessReview review, Guid userId, TimeSpan? after = null, bool failing = false,
        string? appUrl = AppUrl)
    {
        fixture.Clock.UtcNow = review.ReportedAt!.Value + (after ?? TimeSpan.FromMinutes(1));
        fixture.TenantService.SetTenant(tenant.Id);
        var emails = new Emails(fail: failing);
        await new BusinessReviewReportEmailHandler(fixture.Context, emails, new Links(appUrl), fixture.Clock)
            .HandleAsync(new BusinessReviewReportEmailEvent(review.Id, userId), Context(tenant), CancellationToken.None);
        return emails.Sent;
    }
}
