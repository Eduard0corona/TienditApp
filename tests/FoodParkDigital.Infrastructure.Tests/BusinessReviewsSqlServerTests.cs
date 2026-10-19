using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Reviews;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Reviews.Entities;
using FoodParkDigital.Domain.Reviews.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The reviews of the businesses (Fase 24H, ADR 0030) where InMemory proves nothing: the unique index that keeps one review
/// per account and business when two tabs write the first one at once, and the row version that keeps a change saved on
/// what someone else changed meanwhile (the business reporting a review the platform just hid) from overwriting it.
/// </summary>
public class BusinessReviewsSqlServerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 18, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task Write_AtTheSameTimeFromAnotherTab_ShouldKeepOne_AndSayItWasAlreadyWritten()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var store = await SeedStoreWithACustomerWhoBoughtAsync(database);

        // The same account wrote its review from another tab after this save read that it had none.
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        await using var racing = database.CreateProductionLikeContext(tenant, new InMemoryApplicationDbContext.NullMediator(),
            new SavedMeanwhile(async () =>
            {
                await using var other = database.CreateContext(store.TenantId);
                other.BusinessReviews.Add(BusinessReview.Write(store.TenantId, store.UserId, "Mariana O.", 4, "Desde la otra pestaña.", Now));
                await other.SaveChangesAsync();
            }));
        var result = await new CustomerReviewsHandlers(racing, new Customer(store.UserId), tenant, new Clock())
            .Handle(new SaveMyReviewCommand(store.Slug, 5, "Muy rico."), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ReviewRules.WrittenMeanwhileMessage, result.Error);
        await using var check = database.CreateContext(store.TenantId);
        var kept = await check.BusinessReviews.SingleAsync();
        Assert.Equal("Desde la otra pestaña.", kept.Comment);
    }

    [SqlServerFact]
    public async Task Report_WhileThePlatformHidesIt_ShouldBeAConflict_AndKeepThePlatformsDecision()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var store = await SeedStoreWithACustomerWhoBoughtAsync(database);
        Guid reviewId;
        await using (var seed = database.CreateContext(store.TenantId))
        {
            var review = BusinessReview.Write(store.TenantId, store.UserId, "Mariana O.", 1, "Vengan a mi cafetería.", Now);
            seed.BusinessReviews.Add(review);
            await seed.SaveChangesAsync();
            reviewId = review.Id;
        }

        // The business read the review published; the platform hides it before the report is written.
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        tenant.SetTenant(store.TenantId);
        await using var racing = database.CreateProductionLikeContext(tenant, new InMemoryApplicationDbContext.NullMediator(),
            new SavedMeanwhile(async () =>
            {
                await using var platform = database.CreateContext(store.TenantId);
                var review = await platform.BusinessReviews.SingleAsync(r => r.Id == reviewId);
                review.Hide(ReviewConcern.Spam, Now);
                await platform.SaveChangesAsync();
            }));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => new BusinessReviewsHandlers(racing, new Clock())
            .Handle(new ReportReviewCommand(reviewId, ReviewConcern.NotAnExperience), CancellationToken.None));

        await using var check = database.CreateContext(store.TenantId);
        var stored = await check.BusinessReviews.SingleAsync(r => r.Id == reviewId);
        Assert.Equal(ReviewStatus.Hidden, stored.Status);
        Assert.Equal(ReviewConcern.Spam, stored.HiddenReason);
        Assert.Null(stored.ReportedAt);
    }

    private sealed record Store(Guid TenantId, string Slug, Guid UserId);

    /// <summary>A store with its menu published and a customer account that received an order from it.</summary>
    private static async Task<Store> SeedStoreWithACustomerWhoBoughtAsync(SqlServerTestDatabase database)
    {
        var slug = $"cafe-{Guid.NewGuid():N}"[..20];
        var business = new Tenant("Café Luna", slug, BusinessType.Food, $"{slug}@demo.com");
        var settings = new TenantSettings(business.Id, TenantCapability.Food);
        settings.UpdateOnlineMenu(OnlineMenuMode.Orders);
        var customer = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"{slug}@cliente.mx",
            NormalizedUserName = $"{slug}@CLIENTE.MX",
            Email = $"{slug}@cliente.mx",
            NormalizedEmail = $"{slug}@CLIENTE.MX",
            FirstName = "Mariana",
            LastName = "Ortiz",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Role = UserRole.Customer,
            IsActive = true
        };
        var order = new Order(business.Id, null, null, true, "ORD-000001", BusinessType.Food, OrderType.Retail, "Mariana", "+525522223333", null, null);
        order.AddItem(Guid.NewGuid(), "Latte", 1, 55m);
        order.TransitionTo(OrderStatus.Delivered, OrderStateMachineFactory.Create(BusinessType.Food, OrderType.Retail), "cajero");

        await using var seed = database.CreateContext(business.Id);
        seed.AddRange(business, settings);
        seed.Users.Add(customer);
        seed.Orders.Add(order);
        await seed.SaveChangesAsync();
        // The order placed with the account's session (Fase 24B).
        await seed.Database.ExecuteSqlAsync($"UPDATE [Orders] SET [CustomerUserId] = {customer.Id} WHERE [Id] = {order.Id}");
        return new Store(business.Id, slug, customer.Id);
    }

    /// <summary>The customer account signed in.</summary>
    private sealed class Customer(Guid userId) : ICurrentUserService
    {
        public string? UserId => userId.ToString();
        public string? UserName => "cliente@demo.com";
        public string? Role => "CUSTOMER";
        public string? TenantRole => null;
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => Now;
    }
}
