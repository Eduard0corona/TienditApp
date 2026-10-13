using System.Data.Common;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Finance.Queries;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The cuts and the shift history name their people in SQL Server as its tenant filter and collation leave them: only
/// members of the business in scope, an email whatever its case, a name shared only within the business (whatever its
/// case), and one lookup of users for the whole response.
/// </summary>
public class CashPeopleSqlServerTests
{
    // Noon in Mexico City, the zone of a business that sets none.
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 5);

    [SqlServerFact]
    public async Task ShiftHistory_ShouldNameOnlyMembersOfTheBusiness_InOneLookup()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var businesses = await SeedAsync(database);
        var lookups = new UserLookups();
        await using var context = Context(database, businesses.Cafe, lookups);

        var history = (await new GetCashSessionsQueryHandler(context, new Viewer())
                .Handle(new GetCashSessionsQuery(), CancellationToken.None)).Items
            .ToDictionary(s => s.OpenedBy);

        Assert.Equal(4, history.Count);
        // The email of her token, in another case than her account's.
        Assert.Equal(("Mariana Ortiz", "Carlos Rivas"), (history["MARIANA@Cafe.test"].OpenedByName, history["MARIANA@Cafe.test"].ClosedByName));
        // Lucía is a member of the other business only, with her id on the drawer.
        Assert.Null(history["lucia@otro.test"].OpenedByName);
        Assert.Null(history["system-migration"].OpenedByName);
        // "Juan Pérez" and "JUAN PÉREZ" are the same name to SQL Server: two members share it.
        Assert.Null(history["juan@cafe.test"].OpenedByName);
        Assert.Equal(1, lookups.Count);
    }

    [SqlServerFact]
    public async Task DailyCut_ShouldNameAndFindOnlyMembersOfTheBusiness()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var businesses = await SeedAsync(database);
        var lookups = new UserLookups();
        await using var context = Context(database, businesses.Cafe, lookups);
        var handler = new GetDailyCashCutQueryHandler(context, Tenant(businesses.Cafe), new Clock());

        var cut = await handler.Handle(new GetDailyCashCutQuery { Date = Today }, CancellationToken.None);
        var lookupsOfTheCut = lookups.Count;
        var byName = await handler.Handle(new GetDailyCashCutQuery { Date = Today, UserName = "mariana ortiz" }, CancellationToken.None);
        var outsider = await handler.Handle(new GetDailyCashCutQuery { Date = Today, UserName = "Lucía Gómez" }, CancellationToken.None);

        Assert.Equal(
            [("Sistema", null), ("lucia@otro.test", null), ("MARIANA@Cafe.test", "Mariana Ortiz")],
            cut.ByUser.Select(u => (u.UserName, u.DisplayName)));
        Assert.Equal(1, lookupsOfTheCut);
        // Her payment was signed with another case than her account's email: SQL Server still finds it by her name.
        var mine = Assert.Single(byName.ByUser);
        Assert.Equal(("Mariana Ortiz", 1, 34.80m), (mine.DisplayName, mine.PaymentsCount, mine.TotalAmount));
        Assert.Equal(5m, byName.ManualIncomeTotal);
        Assert.Equal((0, 0m), (outsider.SalesPaymentsCount, outsider.ManualIncomeTotal));
    }

    private sealed record Businesses(Guid Cafe, Guid Other);

    /// <summary>
    /// A café with Mariana, Carlos and two Juan Pérez, and another business with Lucía and a Mariana Ortiz of its own;
    /// drawers and payments of the café signed by its members, by Lucía and by the system.
    /// </summary>
    private static async Task<Businesses> SeedAsync(SqlServerTestDatabase database)
    {
        var cafe = new Tenant("Café", $"cafe-{Guid.NewGuid():N}"[..20], BusinessType.Retail, "cafe@demo.com");
        var other = new Tenant("Otro", $"otro-{Guid.NewGuid():N}"[..20], BusinessType.Retail, "otro@demo.com");
        var mariana = NewUser("Mariana", "Ortiz", "mariana@cafe.test");
        var carlos = NewUser("Carlos", "Rivas", "carlos@cafe.test");
        var juan = NewUser("Juan", "Pérez", "juan@cafe.test");
        var otherJuan = NewUser("JUAN", "PÉREZ", "juanp@cafe.test");
        var lucia = NewUser("Lucía", "Gómez", "lucia@otro.test");
        var otherMariana = NewUser("Mariana", "Ortiz", "mariana@otro.test");

        var signed = new CashSession(cafe.Id, Now.AddHours(-4), "MARIANA@Cafe.test", 500m, openedByUserId: null);
        signed.RegisterTransaction(CashTransactionType.Deposit, 5m, Now.AddHours(-3)).CreatedBy = "mariana@cafe.test";
        signed.Close(Now.AddHours(-1), "carlos@cafe.test", 505m);
        var luciasDrawer = new CashSession(cafe.Id, Now.AddHours(-3), "lucia@otro.test", 100m, openedByUserId: lucia.Id);
        luciasDrawer.RegisterTransaction(CashTransactionType.Deposit, 7m, Now.AddHours(-2)).CreatedBy = "lucia@otro.test";
        var migrated = new CashSession(cafe.Id, Now.AddDays(-30), "system-migration", 0m);
        var juans = new CashSession(cafe.Id, Now.AddHours(-2), "juan@cafe.test", 100m, openedByUserId: juan.Id);

        var number = 0;
        // A sale of $amount plus IVA, paid whole.
        Order Paid(decimal amount, string? createdBy)
        {
            var order = new Order(cafe.Id, null, null, true, $"ORD-{++number:D6}", BusinessType.Retail, OrderType.Retail,
                "Cliente", null, null, null);
            order.AddItem(Guid.NewGuid(), "Refresco", 1, amount);
            order.RegisterPayment(order.Total, OrderPaymentMethod.Cash, Now.AddHours(-number), null, null, null, null).CreatedBy = createdBy;
            return order;
        }

        await using var context = database.CreateContext();
        context.Tenants.AddRange(cafe, other);
        context.TenantSettings.AddRange(new TenantSettings(cafe.Id, TenantCapability.Retail), new TenantSettings(other.Id, TenantCapability.Retail));
        context.Users.AddRange(mariana, carlos, juan, otherJuan, lucia, otherMariana);
        context.TenantUsers.AddRange(
            new TenantUser(cafe.Id, mariana.Id, TenantUserRole.Staff),
            new TenantUser(cafe.Id, carlos.Id, TenantUserRole.Admin),
            new TenantUser(cafe.Id, juan.Id, TenantUserRole.Staff),
            new TenantUser(cafe.Id, otherJuan.Id, TenantUserRole.Staff),
            new TenantUser(other.Id, lucia.Id, TenantUserRole.Owner),
            new TenantUser(other.Id, otherMariana.Id, TenantUserRole.Staff));
        context.CashSessions.AddRange(signed, luciasDrawer, migrated, juans);
        context.Orders.AddRange(Paid(30m, "MARIANA@Cafe.test"), Paid(40m, "lucia@otro.test"), Paid(50m, null));
        await context.SaveChangesAsync();
        return new Businesses(cafe.Id, other.Id);
    }

    private static ApplicationUser NewUser(string firstName, string lastName, string email) => new()
    {
        Id = Guid.NewGuid(),
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        FirstName = firstName,
        LastName = lastName,
        SecurityStamp = Guid.NewGuid().ToString("N"),
        Role = UserRole.Staff,
        IsActive = true
    };

    private static ApplicationDbContext Context(SqlServerTestDatabase database, Guid tenantId, UserLookups lookups)
        => database.CreateProductionLikeContext(Tenant(tenantId), new InMemoryApplicationDbContext.NullMediator(), lookups);

    private static InMemoryApplicationDbContext.TestCurrentTenantService Tenant(Guid tenantId)
    {
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        tenant.SetTenant(tenantId);
        return tenant;
    }

    /// <summary>Counts the statements that read the users.</summary>
    private sealed class UserLookups : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("[AspNetUsers]", StringComparison.Ordinal))
                Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class Viewer : ICurrentUserService
    {
        public string? UserId => Guid.NewGuid().ToString();
        public string? UserName => "carlos@cafe.test";
        public string? Role => "TENANT_ADMIN";
        public string? TenantRole => "Admin";
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => Now;
    }
}
