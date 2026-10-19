using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Application.Reviews;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Reviews.Entities;
using FoodParkDigital.Domain.Reviews.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Reviews (Fase 24H, ADR 0030): a customer account reviews a business only after it received an order or attended an
/// appointment there, and changes or deletes its review; the store and Explorar show the published ones and their average
/// from three; the business answers and reports without taking a review down; and the platform hides, keeps or restores
/// it, audited in its business.
/// </summary>
public class BusinessReviewsFlowTests
{
    [Fact]
    public async Task Customer_ShouldReviewABusiness_OnlyAfterAnOrderItReceived_AndChangeOrDeleteIt()
    {
        await using var fixture = SalesTestFixture.Create();
        var store = await StoreAsync(fixture, "cafe");
        var mariana = await AccountAsync(fixture, "mariana@demo.com");
        SignIn(fixture, mariana);

        Assert.Equal((false, (MyReviewDto?)null), Of(await MineAsync(fixture, "cafe")));
        Assert.Equal(ReviewRules.NotACustomerYetMessage, (await SaveAsync(fixture, "cafe", 5, "Muy rico")).Error);

        // An order with her phone that is not the account's (taken at the counter) does not count: only the account's do.
        await OrderAsync(fixture, store, mariana, OrderStatus.Delivered, asGuest: true);
        Assert.False((await MineAsync(fixture, "cafe"))!.CanReview);

        // An order on its way is not yet an experience; a cancelled one never is.
        var order = await OrderAsync(fixture, store, mariana, OrderStatus.InPreparation);
        await OrderAsync(fixture, store, mariana, OrderStatus.Cancelled);
        Assert.False((await MineAsync(fixture, "cafe"))!.CanReview);
        await SetStatusAsync(fixture, order, OrderStatus.Delivered);
        Assert.True((await MineAsync(fixture, "cafe"))!.CanReview);

        var written = await SaveAsync(fixture, "cafe", 5, "  Muy rico, llegó caliente.  ");
        Assert.True(written.IsSuccess, written.Error);
        Assert.Equal(("cafe", "Negocio cafe", 5, "Muy rico, llegó caliente.", false), (written.Value.Slug, written.Value.BusinessName, written.Value.Rating, written.Value.Comment, written.Value.Hidden));
        var stored = await fixture.Context.BusinessReviews.IgnoreQueryFilters().SingleAsync();
        Assert.Equal((store.Id, mariana.Id, "Mariana O."), (stored.TenantId, stored.UserId, stored.AuthorName));

        // Writing again changes the same review.
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddDays(1);
        var changed = await SaveAsync(fixture, "cafe", 3, null);
        Assert.True(changed.IsSuccess, changed.Error);
        Assert.Equal((3, (string?)null, SalesTestFixture.Now.AddDays(1)), (changed.Value.Rating, changed.Value.Comment, changed.Value.EditedAt));
        Assert.Single(await fixture.Context.BusinessReviews.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(["cafe"], (await MyReviewsAsync(fixture)).Select(r => r.Slug));

        Assert.True((await DeleteAsync(fixture, "cafe")).IsSuccess);
        Assert.Empty(await fixture.Context.BusinessReviews.IgnoreQueryFilters().ToListAsync());
        // Deleting what is not there changes nothing.
        Assert.True((await DeleteAsync(fixture, "cafe")).IsSuccess);
    }

    [Fact]
    public async Task Customer_ShouldReviewABusiness_AfterAnAppointmentItAttended()
    {
        await using var fixture = SalesTestFixture.Create();
        var store = await StoreAsync(fixture, "estetica");
        var mariana = await AccountAsync(fixture, "mariana@demo.com");
        SignIn(fixture, mariana);
        var appointment = Appointment.RequestOnline(
            store.Id, "Mariana Ortiz", "5512345678", Guid.NewGuid(), "Corte", SalesTestFixture.Now, SalesTestFixture.Now.AddHours(1),
            null, mariana.Id, null, new string('a', Order.TrackingTokenLength));
        fixture.Context.Appointments.Add(appointment);
        await fixture.Context.SaveChangesAsync();
        Assert.False((await MineAsync(fixture, "estetica"))!.CanReview);

        var attended = await fixture.Context.Appointments.IgnoreQueryFilters().SingleAsync(a => a.Id == appointment.Id);
        fixture.Context.Entry(attended).Property(a => a.Status).CurrentValue = AppointmentStatus.Completed;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        Assert.True((await MineAsync(fixture, "estetica"))!.CanReview);
        Assert.True((await SaveAsync(fixture, "estetica", 4, "Buen corte")).IsSuccess);
    }

    [Fact]
    public async Task Save_ShouldRefuse_ATeamAccount_AStoreNotPublished_AndALinkInTheComment()
    {
        await using var fixture = SalesTestFixture.Create();
        var store = await StoreAsync(fixture, "cafe");
        var owner = await AccountAsync(fixture, "dueno@demo.com", UserRole.TenantAdmin);
        SignIn(fixture, owner);
        Assert.Equal(ReviewRules.CustomersOnlyMessage, (await SaveAsync(fixture, "cafe", 5, null)).Error);
        Assert.Equal((false, (MyReviewDto?)null), Of(await MineAsync(fixture, "cafe")));
        Assert.Empty(await MyReviewsAsync(fixture));

        var mariana = await AccountAsync(fixture, "mariana@demo.com");
        SignIn(fixture, mariana);
        await OrderAsync(fixture, store, mariana, OrderStatus.Delivered);
        Assert.Equal(BusinessReview.CommentContactMessage, (await SaveAsync(fixture, "cafe", 5, "Mejor pidan en otra-tienda.com")).Error);

        var settings = await fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == store.Id);
        settings.UpdateOnlineMenu(OnlineMenuMode.Off);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(ReviewRules.StoreNotFoundMessage, (await SaveAsync(fixture, "cafe", 5, null)).Error);
        Assert.Null(await MineAsync(fixture, "cafe"));
        Assert.Null(await PublicAsync(fixture, "cafe"));
        Assert.Empty(await fixture.Context.BusinessReviews.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Store_ShouldShowThePublishedReviews_NewestFirst_AndTheirAverageFromThree()
    {
        await using var fixture = SalesTestFixture.Create();
        var store = await StoreAsync(fixture, "cafe");
        await ReviewAsync(fixture, store, 5, "Uno", days: 1);
        await ReviewAsync(fixture, store, 4, "Dos", days: 2);

        // Two reviews say little: their count, no average.
        var two = await PublicAsync(fixture, "cafe");
        Assert.Equal((2, (decimal?)null), (two!.Summary.Count, two.Summary.Average));
        Assert.Equal(["Dos", "Uno"], two.Items.Select(r => r.Comment));
        Assert.Equal((2, (decimal?)null), ((await MenuAsync(fixture, "cafe"))!.Rating!.Count, (await MenuAsync(fixture, "cafe"))!.Rating!.Average));

        var hidden = await ReviewAsync(fixture, store, 1, "Oculta", days: 3);
        hidden.Hide(ReviewConcern.Offensive, SalesTestFixture.Now);
        await fixture.Context.SaveChangesAsync();
        var latest = await ReviewAsync(fixture, store, 4, "Tres", days: 4, edited: true);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var three = await PublicAsync(fixture, "cafe");
        // 5 + 4 + 4 = 13 / 3 = 4.333: one decimal. The hidden one is in neither.
        Assert.Equal((3, (decimal?)4.3m), (three!.Summary.Count, three.Summary.Average));
        Assert.Equal(["Tres", "Dos", "Uno"], three.Items.Select(r => r.Comment));
        Assert.Equal((true, latest.AuthorName), (three.Items[0].Edited, three.Items[0].AuthorName));
        Assert.False(three.HasNextPage);
        Assert.Equal(4.3m, (await MenuAsync(fixture, "cafe"))!.Rating!.Average);
        var card = Assert.Single((await DirectoryAsync(fixture)).Businesses.Items);
        Assert.Equal((3, (decimal?)4.3m), (card.Rating!.Count, card.Rating.Average));

        // Ten at a time.
        for (var i = 0; i < 9; i++)
            await ReviewAsync(fixture, store, 5, $"Más {i}", days: 10 + i);
        var first = await PublicAsync(fixture, "cafe");
        var second = await PublicAsync(fixture, "cafe", page: 2);
        Assert.Equal((10, true, 2, false), (first!.Items.Count, first.HasNextPage, second!.Items.Count, second.HasNextPage));

        // Without reviews a store has no rating.
        await StoreAsync(fixture, "nueva");
        Assert.Null((await MenuAsync(fixture, "nueva"))!.Rating);
        Assert.Equal((0, 0), ((await PublicAsync(fixture, "nueva"))!.Summary.Count, (await PublicAsync(fixture, "nueva"))!.Items.Count));
    }

    [Fact]
    public async Task Business_ShouldAnswerAndReport_ItsOwnReviews_WithoutTakingThemDown()
    {
        await using var fixture = SalesTestFixture.Create();
        var other = await StoreAsync(fixture, "otro");
        var foreign = await ReviewAsync(fixture, other, 1, "De otro negocio", days: 1);
        var store = await StoreAsync(fixture, "cafe");
        var good = await ReviewAsync(fixture, store, 5, "Excelente", days: 2);
        var bad = await ReviewAsync(fixture, store, 1, "Nunca llegó", days: 3);
        var handlers = new BusinessReviewsHandlers(fixture.Context, fixture.Clock);

        var replied = await handlers.Handle(new ReplyToReviewCommand(good.Id, "¡Gracias, te esperamos!"), CancellationToken.None);
        Assert.True(replied.IsSuccess, replied.Error);
        Assert.Equal(("¡Gracias, te esperamos!", SalesTestFixture.Now), (replied.Value.Reply, replied.Value.RepliedAt));

        var reported = await handlers.Handle(new ReportReviewCommand(bad.Id, ReviewConcern.NotAnExperience), CancellationToken.None);
        Assert.True(reported.IsSuccess, reported.Error);
        Assert.Equal((ReviewStatus.Published, ReviewConcern.NotAnExperience), (reported.Value.Status, reported.Value.ReportReason));
        // Still in the store: a business never takes down what a customer said.
        Assert.Equal(2, (await PublicAsync(fixture, "cafe"))!.Summary.Count);
        fixture.TenantService.SetTenant(store.Id);

        var all = await handlers.Handle(new GetBusinessReviewsQuery(), CancellationToken.None);
        Assert.Equal((2, 1, 1), (all.Reviews.TotalCount, all.Unanswered, all.Reported));
        Assert.Equal(["Nunca llegó", "Excelente"], all.Reviews.Items.Select(r => r.Comment));
        Assert.Equal(["Nunca llegó"], (await handlers.Handle(new GetBusinessReviewsQuery(BusinessReviewsFilter.Unanswered), CancellationToken.None)).Reviews.Items.Select(r => r.Comment));
        Assert.Equal(["Nunca llegó"], (await handlers.Handle(new GetBusinessReviewsQuery(BusinessReviewsFilter.Reported), CancellationToken.None)).Reviews.Items.Select(r => r.Comment));

        Assert.Equal(BusinessReview.AlreadyReportedMessage,
            (await handlers.Handle(new ReportReviewCommand(bad.Id, ReviewConcern.Spam), CancellationToken.None)).Error);
        Assert.Equal(BusinessReview.ReplyContactMessage,
            (await handlers.Handle(new ReplyToReviewCommand(bad.Id, "Llámanos al 222 123 4567"), CancellationToken.None)).Error);
        var removed = await handlers.Handle(new RemoveReviewReplyCommand(good.Id), CancellationToken.None);
        Assert.Null(removed.Value.Reply);

        // Another business's review does not exist here.
        Assert.Equal(ReviewRules.NotFoundMessage, (await handlers.Handle(new ReplyToReviewCommand(foreign.Id, "Gracias"), CancellationToken.None)).Error);
        Assert.Equal(ReviewRules.NotFoundMessage, (await handlers.Handle(new ReportReviewCommand(foreign.Id, ReviewConcern.Spam), CancellationToken.None)).Error);
    }

    [Fact]
    public async Task Platform_ShouldHideKeepOrRestore_ReportedReviews_AuditedInTheirBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        var taller = await StoreAsync(fixture, "taller");
        var cafe = await StoreAsync(fixture, "cafe");
        var other = await ReviewAsync(fixture, taller, 1, "Del taller", days: 4);
        var offensive = await ReviewAsync(fixture, cafe, 1, "Ofensiva", days: 1);
        var fair = await ReviewAsync(fixture, cafe, 2, "Tardaron", days: 2);
        await ReviewAsync(fixture, cafe, 5, "Rico", days: 3);
        foreach (var (written, at) in new[] { (fair, 2), (offensive, 1), (other, 3) })
        {
            var review = await fixture.Context.BusinessReviews.IgnoreQueryFilters().SingleAsync(r => r.Id == written.Id);
            review.Report(ReviewConcern.Offensive, SalesTestFixture.Now.AddHours(at));
        }
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        // The platform admin is in no business.
        fixture.TenantService.SetTenant(Guid.Empty);
        fixture.CurrentUser.UserName = "plataforma@tienditapp.mx";
        var handlers = new PlatformReviewsHandlers(fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock);

        // The reported ones of every business, the oldest report first.
        var reported = await handlers.Handle(new GetPlatformReviewsQuery(), CancellationToken.None);
        Assert.Equal(["Ofensiva", "Tardaron", "Del taller"], reported.Items.Select(r => r.Comment));
        Assert.Equal(["Negocio cafe", "Negocio cafe", "Negocio taller"], reported.Items.Select(r => r.BusinessName));

        Assert.True((await handlers.Handle(new HideReviewCommand(offensive.Id, ReviewConcern.Offensive), CancellationToken.None)).IsSuccess);
        Assert.True((await handlers.Handle(new KeepReviewCommand(fair.Id), CancellationToken.None)).IsSuccess);
        Assert.Equal(BusinessReview.NotReportedMessage, (await handlers.Handle(new KeepReviewCommand(fair.Id), CancellationToken.None)).Error);
        Assert.Equal(ReviewRules.NotFoundMessage, (await handlers.Handle(new KeepReviewCommand(Guid.NewGuid()), CancellationToken.None)).Error);

        // Out of the store and its average; its author sees why.
        var store = await PublicAsync(fixture, "cafe");
        Assert.Equal(2, store!.Summary.Count);
        Assert.Equal(["Rico", "Tardaron"], store.Items.Select(r => r.Comment));
        var audits = await fixture.Context.AuditLogs.IgnoreQueryFilters().Where(a => a.EntityName == nameof(BusinessReview)).OrderBy(a => a.Action).ToListAsync();
        Assert.Equal(
            [("ReviewHidden", cafe.Id, offensive.Id.ToString(), "plataforma@tienditapp.mx"), ("ReviewKept", cafe.Id, fair.Id.ToString(), "plataforma@tienditapp.mx")],
            audits.Select(a => (a.Action, a.TenantId, a.EntityId, a.PerformedBy)));
        // What the customer wrote stays out of the entry.
        Assert.DoesNotContain(audits, a => a.Metadata?.Contains("Ofensiva") == true);

        fixture.TenantService.SetTenant(Guid.Empty);
        var hidden = await handlers.Handle(new GetPlatformReviewsQuery(PlatformReviewsFilter.Hidden), CancellationToken.None);
        Assert.Equal(["Ofensiva"], hidden.Items.Select(r => r.Comment));
        Assert.True((await handlers.Handle(new RestoreReviewCommand(offensive.Id), CancellationToken.None)).IsSuccess);
        Assert.Equal(3, (await PublicAsync(fixture, "cafe"))!.Summary.Count);
    }

    private static (bool, MyReviewDto?) Of(MyReviewOfBusinessDto? dto) => (dto!.CanReview, dto.Review);

    /// <summary>A store with its menu published, listed in Explorar with one product.</summary>
    private static async Task<Tenant> StoreAsync(SalesTestFixture fixture, string slug)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: slug);
        var settings = await fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.Id);
        settings.UpdateOnlineMenu(OnlineMenuMode.MenuOnly);
        var category = new Category(tenant.Id, "General", null);
        fixture.Context.Categories.Add(category);
        fixture.Context.Products.Add(new Product(tenant.Id, category.Id, "Café americano", null, 40m, "MXN"));
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return tenant;
    }

    private static async Task<ApplicationUser> AccountAsync(SalesTestFixture fixture, string email, UserRole role = UserRole.Customer)
    {
        var account = new ApplicationUser { UserName = email, Email = email, FirstName = "Mariana", LastName = "Ortiz", Role = role, IsActive = true };
        fixture.Context.Users.Add(account);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return account;
    }

    private static void SignIn(SalesTestFixture fixture, ApplicationUser account)
    {
        fixture.CurrentUser.UserId = account.Id.ToString();
        fixture.CurrentUser.Role = account.Role == UserRole.Customer ? "CUSTOMER" : "TENANT_ADMIN";
    }

    /// <summary>
    /// An order of the account at the business, already in <paramref name="status"/>; <paramref name="asGuest"/> is one the
    /// business took with the account's name and phone, not linked to the account.
    /// </summary>
    private static async Task<Order> OrderAsync(
        SalesTestFixture fixture, Tenant business, ApplicationUser account, OrderStatus status, bool asGuest = false)
    {
        fixture.TenantService.SetTenant(business.Id);
        var order = new Order(
            business.Id, asGuest ? null : account.Id, temporaryCustomerId: null, createdByBusiness: asGuest, $"F-{Guid.NewGuid():N}"[..10],
            BusinessType.Food, OrderType.Retail, "Mariana Ortiz", "5512345678", null, null, channel: OrderChannel.Online);
        fixture.Context.Orders.Add(order);
        await fixture.Context.SaveChangesAsync();
        await SetStatusAsync(fixture, order, status);
        return order;
    }

    private static async Task SetStatusAsync(SalesTestFixture fixture, Order order, OrderStatus status)
    {
        var stored = await fixture.Context.Orders.IgnoreQueryFilters().SingleAsync(o => o.Id == order.Id);
        fixture.Context.Entry(stored).Property(o => o.Status).CurrentValue = status;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
    }

    private static int _authors;

    /// <summary>A review of the business by an account of its own, written <paramref name="days"/> after the fixture's now.</summary>
    private static async Task<BusinessReview> ReviewAsync(
        SalesTestFixture fixture, Tenant business, int rating, string comment, int days, bool edited = false)
    {
        var number = Interlocked.Increment(ref _authors);
        var account = await AccountAsync(fixture, $"cliente{number}@demo.com");
        var review = BusinessReview.Write(business.Id, account.Id, $"Cliente {number} O.", rating, comment, SalesTestFixture.Now.AddDays(days));
        if (edited)
            review.Edit($"Cliente {number} O.", rating, comment, SalesTestFixture.Now.AddDays(days + 1));
        fixture.TenantService.SetTenant(business.Id);
        fixture.Context.BusinessReviews.Add(review);
        await fixture.Context.SaveChangesAsync();
        return review;
    }

    private static CustomerReviewsHandlers Customer(SalesTestFixture fixture)
        => new(fixture.Context, fixture.CurrentUser, fixture.TenantService, fixture.Clock);

    private static async Task<MyReviewOfBusinessDto?> MineAsync(SalesTestFixture fixture, string slug)
    {
        var result = await Customer(fixture).Handle(new GetMyReviewOfBusinessQuery(slug), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static async Task<List<MyReviewDto>> MyReviewsAsync(SalesTestFixture fixture)
        => await Customer(fixture).Handle(new GetMyReviewsQuery(), CancellationToken.None);

    private static async Task<Result<MyReviewDto>> SaveAsync(SalesTestFixture fixture, string slug, int rating, string? comment)
    {
        var result = await Customer(fixture).Handle(new SaveMyReviewCommand(slug, rating, comment), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static async Task<Result> DeleteAsync(SalesTestFixture fixture, string slug)
    {
        var result = await Customer(fixture).Handle(new DeleteMyReviewCommand(slug), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static Task<PublicReviewsDto?> PublicAsync(SalesTestFixture fixture, string slug, int page = 1)
        => new GetPublicReviewsQueryHandler(fixture.Context, fixture.TenantService).Handle(new GetPublicReviewsQuery(slug, page), CancellationToken.None);

    private static Task<PublicMenuDto?> MenuAsync(SalesTestFixture fixture, string slug)
        => new GetPublicMenuQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock, FakeOnlinePaymentGateway.Off).Handle(new GetPublicMenuQuery(slug), CancellationToken.None);

    private static Task<PublicDirectoryDto> DirectoryAsync(SalesTestFixture fixture)
        => new GetPublicDirectoryQueryHandler(
                fixture.Context, fixture.Clock, new PublicDirectoryCache(), new PublicProductIndexCache(), NullLogger<GetPublicDirectoryQueryHandler>.Instance)
            .Handle(new GetPublicDirectoryQuery(), CancellationToken.None);
}
