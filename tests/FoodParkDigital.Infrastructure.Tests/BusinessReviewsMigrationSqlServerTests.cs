using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Reviews.Entities;
using FoodParkDigital.Domain.Reviews.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// AddBusinessReviews (Fase 24H, ADR 0030): one review per account and business, gone with the account and never in the
/// way of a business; the database refuses what the screens rely on never seeing (six stars, a report without its reason
/// or on a hidden review, a hidden review without its reason, an answer without its date); and rolling back stops with
/// error 50023 once a customer wrote one, and otherwise drops only the table.
/// </summary>
public class BusinessReviewsMigrationSqlServerTests
{
    // The migration right before AddBusinessReviews, where its rollback.sql takes the database.
    private const string BeforeReviews = "20261018114538_AddBranchLocation";
    private const string Migration = "20261018141704_AddBusinessReviews";

    [SqlServerFact]
    public async Task Table_ShouldKeepOneReviewPerAccountAndBusiness_AndTheReviewsGoWithTheAccount()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var (userId, businessId, _) = await SeedReviewAsync(database);

        var duplicate = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database, $"""
            INSERT INTO [BusinessReviews] ([Id], [TenantId], [UserId], [AuthorName], [Rating], [WrittenAt], [Status], [CreatedAt])
            VALUES ({Guid.NewGuid()}, {businessId}, {userId}, N'Mariana O.', 3, SYSDATETIMEOFFSET(), 0, SYSDATETIMEOFFSET())
            """));
        Assert.Equal(2601, duplicate.Number);
        Assert.Contains("IX_BusinessReviews_UserId_TenantId", duplicate.Message);

        // A business is never deleted; its reviews would not let it.
        var deleteBusiness = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database,
            $"DELETE FROM [Tenants] WHERE [Id] = {businessId}"));
        Assert.Equal(547, deleteBusiness.Number);

        await ExecuteAsync(database, $"DELETE FROM [AspNetUsers] WHERE [Id] = {userId}");
        await using var check = database.CreateContext();
        Assert.Equal(0, await CountAsync(check, $"SELECT COUNT(*) AS [Value] FROM [BusinessReviews]"));
    }

    [SqlServerFact]
    public async Task Database_ShouldRefuse_WhatAReviewCanNeverBe()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var (_, _, reviewId) = await SeedReviewAsync(database);

        foreach (var (update, constraint) in new[]
                 {
                     ("UPDATE [BusinessReviews] SET [Rating] = 6 WHERE [Id] = {0}", "CK_BusinessReviews_Rating"),
                     ("UPDATE [BusinessReviews] SET [Rating] = 0 WHERE [Id] = {0}", "CK_BusinessReviews_Rating"),
                     // A status other than published or hidden fails the rule of hidden reviews first.
                     ("UPDATE [BusinessReviews] SET [Status] = 2, [HiddenReason] = 1 WHERE [Id] = {0}", "CK_BusinessReviews_"),
                     ("UPDATE [BusinessReviews] SET [ReportedAt] = SYSDATETIMEOFFSET() WHERE [Id] = {0}", "CK_BusinessReviews_Report"),
                     ("UPDATE [BusinessReviews] SET [ReportReason] = 1 WHERE [Id] = {0}", "CK_BusinessReviews_Report"),
                     ("UPDATE [BusinessReviews] SET [ReportedAt] = SYSDATETIMEOFFSET(), [ReportReason] = 6 WHERE [Id] = {0}", "CK_BusinessReviews_Report"),
                     ("UPDATE [BusinessReviews] SET [Status] = 1 WHERE [Id] = {0}", "CK_BusinessReviews_Hidden"),
                     ("UPDATE [BusinessReviews] SET [HiddenReason] = 1 WHERE [Id] = {0}", "CK_BusinessReviews_Hidden"),
                     ("UPDATE [BusinessReviews] SET [Reply] = N'Gracias' WHERE [Id] = {0}", "CK_BusinessReviews_Reply"),
                     ("UPDATE [BusinessReviews] SET [RepliedAt] = SYSDATETIMEOFFSET() WHERE [Id] = {0}", "CK_BusinessReviews_Reply"),
                     // A hidden review is not reported: the platform already decided.
                     ("UPDATE [BusinessReviews] SET [Status] = 1, [HiddenReason] = 1, [ReportedAt] = SYSDATETIMEOFFSET(), [ReportReason] = 2 WHERE [Id] = {0}", "CK_BusinessReviews_Report")
                 })
        {
            await using var context = database.CreateContext();
            var error = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(update, reviewId));
            Assert.Equal(547, error.Number);
            Assert.Contains(constraint, error.Message);
        }

        // What the domain does is what the database keeps.
        await using (var context = database.CreateContext())
        {
            var review = await context.BusinessReviews.IgnoreQueryFilters().SingleAsync(r => r.Id == reviewId);
            review.ReplyWith("¡Gracias por venir!", DateTimeOffset.UtcNow);
            review.Report(ReviewConcern.Offensive, DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
            review.Hide(ReviewConcern.Offensive, DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        await using var check = database.CreateContext();
        var stored = await check.BusinessReviews.IgnoreQueryFilters().SingleAsync(r => r.Id == reviewId);
        Assert.Equal((ReviewStatus.Hidden, ReviewConcern.Offensive, "¡Gracias por venir!"), (stored.Status, stored.HiddenReason, stored.Reply));
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceACustomerWroteAReview_AndThenDropOnlyTheTable()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        await SeedReviewAsync(database);

        var error = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeReviews));
        Assert.Equal(50023, error.Number);
        Assert.Contains("Customers reviewed businesses", error.Message);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [BusinessReviews]"));
            await context.Database.ExecuteSqlRawAsync("DELETE FROM [BusinessReviews]");
        }

        await MigrateAsync(database, BeforeReviews);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeReviews, await LatestMigrationAsync(context));
            Assert.Equal((0, 1, 1), (
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] = N'BusinessReviews'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [AspNetUsers]"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [Tenants]")));
        }

        // Rolling back again does nothing, and forward again the table is back.
        await MigrateAsync(database, BeforeReviews);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext();
        Assert.Equal(1, await CountAsync(forward, $"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] = N'BusinessReviews'"));
    }

    /// <summary>A customer account that reviewed a business.</summary>
    private static async Task<(Guid UserId, Guid BusinessId, Guid ReviewId)> SeedReviewAsync(SqlServerTestDatabase database)
    {
        await using var context = database.CreateContext();
        var slug = $"cafe-{Guid.NewGuid():N}"[..20];
        var tenant = new Tenant("Café Luna", slug, BusinessType.Food, $"{slug}@demo.com");
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "cliente@demo.com",
            NormalizedUserName = "CLIENTE@DEMO.COM",
            Email = "cliente@demo.com",
            NormalizedEmail = "CLIENTE@DEMO.COM",
            FirstName = "Mariana",
            LastName = "Ortiz",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Role = UserRole.Customer,
            IsActive = true
        };
        var review = BusinessReview.Write(tenant.Id, user.Id, BusinessReview.AuthorNameFrom(user.FirstName, user.LastName), 5, "Muy rico.", DateTimeOffset.UtcNow);
        context.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Food));
        context.Users.Add(user);
        context.BusinessReviews.Add(review);
        await context.SaveChangesAsync();
        return (user.Id, tenant.Id, review.Id);
    }

    /// <summary>Applies the migrations up to <paramref name="target"/>, as a deployment does.</summary>
    private static async Task MigrateAsync(SqlServerTestDatabase database, string target)
    {
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task ExecuteAsync(SqlServerTestDatabase database, FormattableString sql)
    {
        await using var context = database.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    private static Task<string> LatestMigrationAsync(ApplicationDbContext context) =>
        context.Database.SqlQuery<string>($"""
            SELECT TOP (1) [MigrationId] AS [Value] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC
            """).SingleAsync();

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql) =>
        context.Database.SqlQuery<int>(sql).SingleAsync();
}
