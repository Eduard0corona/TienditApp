using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Notices;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Reviews.Notices;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Reviews.Entities;
using FoodParkDigital.Domain.Reviews.Enums;
using FoodParkDigital.Domain.Reviews.Events;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.Application.Tests.Support.NoticeEmailsSupport;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The email of a new review of the business (Fase 31K, ADR 0091): one per member who gets them and may answer the reviews
/// (its owners and admins), what it says and never says, when it is no longer sent, and each person's own choice.
/// </summary>
public class ReviewEmailTests
{
    [Fact]
    public async Task TheEmails_ShouldGoToWhoGetsThem_AndMayAnswerTheReviews_OnePerPerson()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture, "cafe", "Café Luna");
        var owner = await MemberAsync(fixture, shop, "dueña", TenantUserRole.Owner);
        await MemberAsync(fixture, shop, "socia", TenantUserRole.Owner, chooseReviews: false);
        var admin = await MemberAsync(fixture, shop, "admin", TenantUserRole.Admin, chooseReviews: true);
        // An admin who did not choose them; staff who chose them but do not answer the reviews; someone no longer of the team.
        await MemberAsync(fixture, shop, "gerente", TenantUserRole.Admin);
        await MemberAsync(fixture, shop, "cajero", TenantUserRole.Staff, permissions: [TenantPermission.ManageOrders], chooseReviews: true);
        await MemberAsync(fixture, shop, "antes", TenantUserRole.Admin, chooseReviews: true, status: TenantUserStatus.Disabled);
        await MemberAsync(fixture, shop, "nueva", TenantUserRole.Admin, chooseReviews: true, emailConfirmed: false);
        var other = await ShopAsync(fixture, "otra", "Café Otra");
        await MemberAsync(fixture, other, "otra", TenantUserRole.Owner);
        var review = await ReviewAsync(fixture, shop, 4, "Muy rico.");

        fixture.TenantService.SetTenant(shop.Id);
        var outbox = new Outbox<BusinessReviewEmailEvent>();
        await new BusinessReviewWrittenEmailsHandler(fixture.Context, new MembersAccess(fixture.Context), outbox)
            .HandleAsync(new BusinessReviewWrittenEvent(review.Id, shop.Id), Context(shop), CancellationToken.None);

        Assert.Equal(new[] { owner, admin }.Order(), outbox.Added.Select(q => q.Event.UserId).Order());
        Assert.All(outbox.Added, q => Assert.Equal((review.Id, (Guid?)shop.Id), (q.Event.ReviewId, q.TenantId)));
    }

    [Fact]
    public async Task TheEmail_ShouldSayTheStarsAndWhatItSays_AsItsAuthorShowsInTheStore_Encoded()
    {
        await using var fixture = SalesTestFixture.Create();
        // WebUtility encodes the accents too: the names here have none, so what is checked reads plainly.
        var shop = await ShopAsync(fixture, "cafe", "Cocina <Luna>");
        var owner = await MemberAsync(fixture, shop, "dueña", TenantUserRole.Owner);
        var review = await ReviewAsync(fixture, shop, 2, "El <b>pan</b> llego frio.\nY tarde.", author: "<i>Mariana</i> O.");

        var (to, subject, body) = Assert.Single(await SendAsync(fixture, shop, review, owner));

        Assert.Equal($"dueña.{shop.Slug}@demo.com", to);
        Assert.Equal("Nueva reseña en Cocina <Luna>: 2 estrellas de 5", subject);
        Assert.Contains("<strong>&lt;i&gt;Mariana&lt;/i&gt; O.</strong> calificó a Cocina &lt;Luna&gt; con <strong>2 estrellas de 5</strong>.", body);
        Assert.Contains("El &lt;b&gt;pan&lt;/b&gt; llego frio. Y tarde.</blockquote>", body);
        Assert.Contains($"<a href='{AppUrl}/reviews'>Responder en Reseñas</a>", body);
        Assert.Contains($"<a href='{AppUrl}/profile'>Mi perfil</a>", body);
        Assert.DoesNotContain("<b>", body);
        Assert.DoesNotContain("@demo.com", body);

        // Stars alone, and one star.
        var alone = await ReviewAsync(fixture, shop, 1, null);
        var (_, aloneSubject, aloneBody) = Assert.Single(await SendAsync(fixture, shop, alone, owner));
        Assert.Equal("Nueva reseña en Cocina <Luna>: 1 estrella de 5", aloneSubject);
        Assert.Contains("No escribió un comentario.", aloneBody);
        Assert.DoesNotContain("<blockquote", aloneBody);

        // Without the app's address, no links.
        fixture.TenantService.SetTenant(shop.Id);
        var unlinked = new Emails();
        await new BusinessReviewEmailHandler(fixture.Context, new MembersAccess(fixture.Context), unlinked, new Links(null), fixture.Clock)
            .HandleAsync(new BusinessReviewEmailEvent(review.Id, owner), Context(shop), CancellationToken.None);
        Assert.DoesNotContain("href", Assert.Single(unlinked.Sent).Body);
    }

    [Fact]
    public async Task TheEmail_ShouldNotBeSent_OnceTheReviewIsGoneOrHidden_ADayLater_OrToWhoNoLongerGetsThem()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture, "cafe", "Café Luna");
        var owner = await MemberAsync(fixture, shop, "dueña", TenantUserRole.Owner);
        var admin = await MemberAsync(fixture, shop, "admin", TenantUserRole.Admin, chooseReviews: true);
        var deleted = await ReviewAsync(fixture, shop, 5, "Excelente.");
        var hidden = await ReviewAsync(fixture, shop, 1, "Pésimo.");
        var review = await ReviewAsync(fixture, shop, 4, "Bueno.");
        var stored = await fixture.Context.BusinessReviews.IgnoreQueryFilters().SingleAsync(r => r.Id == hidden.Id);
        stored.Hide(ReviewConcern.Offensive, SalesTestFixture.Now);
        fixture.Context.BusinessReviews.Remove(await fixture.Context.BusinessReviews.IgnoreQueryFilters().SingleAsync(r => r.Id == deleted.Id));
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        Assert.Empty(await SendAsync(fixture, shop, deleted, owner));
        Assert.Empty(await SendAsync(fixture, shop, hidden, owner));
        Assert.Single(await SendAsync(fixture, shop, review, owner, after: TimeSpan.FromDays(1)));
        Assert.Empty(await SendAsync(fixture, shop, review, owner, after: TimeSpan.FromDays(1).Add(TimeSpan.FromSeconds(1))));

        // The admin stopped getting them, or became staff, since it was queued; the owner's business was suspended.
        var membership = await fixture.Context.TenantUsers.IgnoreQueryFilters().SingleAsync(m => m.UserId == admin);
        membership.SetRole(TenantUserRole.Staff);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        Assert.Empty(await SendAsync(fixture, shop, review, admin));
        var business = await fixture.Context.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == shop.Id);
        business.SetStatus(TenantStatus.Suspended);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        Assert.Empty(await SendAsync(fixture, shop, review, owner));
    }

    [Fact]
    public async Task AFailingProvider_ShouldThrow_SoTheOutboxRetriesThatEmailAlone()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture, "cafe", "Café Luna");
        var owner = await MemberAsync(fixture, shop, "dueña", TenantUserRole.Owner);
        var review = await ReviewAsync(fixture, shop, 5, "Excelente.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(fixture, shop, review, owner, failing: true));
    }

    [Fact]
    public async Task EachOwnerOrAdmin_ShouldChooseTheirOwn_AndOnlyTheyAsk()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture, "cafe", "Café Luna");
        var owner = await MemberAsync(fixture, shop, "dueña", TenantUserRole.Owner);
        var admin = await MemberAsync(fixture, shop, "admin", TenantUserRole.Admin);

        var ownerHas = await GetAsync(fixture, shop, owner);
        Assert.Equal((true, $"dueña.{shop.Slug}@demo.com", true), (ownerHas.Value!.Enabled, ownerHas.Value.Email, ownerHas.Value.EmailConfirmed));
        Assert.False((await GetAsync(fixture, shop, admin)).Value!.Enabled);

        Assert.True((await UpdateAsync(fixture, shop, admin, true)).Value!.Enabled);
        Assert.False((await UpdateAsync(fixture, shop, owner, false)).Value!.Enabled);
        Assert.True((await fixture.Context.TenantUsers.IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.UserId == admin)).ReviewEmails);
        Assert.False((await fixture.Context.TenantUsers.IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.UserId == owner)).ReviewEmails);

        // Someone who is not of the business (the platform's admin acting in it) has nothing to choose.
        var stranger = await GetAsync(fixture, shop, Guid.NewGuid());
        Assert.Equal(NoticeEmailPreferences.NotAMemberMessage, stranger.Error);

        // What the reviews' API asks: an owner or admin.
        foreach (var request in new[] { typeof(GetReviewEmailsQuery), typeof(UpdateReviewEmailsCommand) })
            Assert.Single(request.GetCustomAttributes(typeof(RequiresTenantManagerAttribute), false));
    }

    private static async Task<Tenant> ShopAsync(SalesTestFixture fixture, string slug, string name)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true, slug: slug);
        tenant.Update(name, $"{slug}@demo.com", null, null);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return tenant;
    }

    /// <summary>A review of the business by a customer account, written at the fixture's now.</summary>
    private static async Task<BusinessReview> ReviewAsync(SalesTestFixture fixture, Tenant business, int rating, string? comment, string author = "Mariana O.")
    {
        var review = BusinessReview.Write(business.Id, Guid.NewGuid(), author, rating, comment, SalesTestFixture.Now);
        fixture.TenantService.SetTenant(business.Id);
        fixture.Context.BusinessReviews.Add(review);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return review;
    }

    /// <summary>Sends one member's email, <paramref name="after"/> the review was written (a minute by default).</summary>
    private static async Task<List<(string To, string Subject, string Body)>> SendAsync(
        SalesTestFixture fixture, Tenant tenant, BusinessReview review, Guid userId, TimeSpan? after = null, bool failing = false)
    {
        fixture.Clock.UtcNow = SalesTestFixture.Now + (after ?? TimeSpan.FromMinutes(1));
        fixture.TenantService.SetTenant(tenant.Id);
        var emails = new Emails(fail: failing);
        await new BusinessReviewEmailHandler(fixture.Context, new MembersAccess(fixture.Context), emails, new Links(AppUrl), fixture.Clock)
            .HandleAsync(new BusinessReviewEmailEvent(review.Id, userId), Context(tenant), CancellationToken.None);
        return emails.Sent;
    }

    private static async Task<Result<ReviewEmailsDto>> GetAsync(SalesTestFixture fixture, Tenant tenant, Guid userId)
    {
        SignIn(fixture, tenant, userId);
        return await new ReviewEmailsHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser)
            .Handle(new GetReviewEmailsQuery(), CancellationToken.None);
    }

    private static async Task<Result<ReviewEmailsDto>> UpdateAsync(SalesTestFixture fixture, Tenant tenant, Guid userId, bool enabled)
    {
        SignIn(fixture, tenant, userId);
        var result = await new ReviewEmailsHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser)
            .Handle(new UpdateReviewEmailsCommand(enabled), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static void SignIn(SalesTestFixture fixture, Tenant tenant, Guid userId)
    {
        fixture.TenantService.SetTenant(tenant.Id);
        fixture.CurrentUser.UserId = userId.ToString();
    }
}
