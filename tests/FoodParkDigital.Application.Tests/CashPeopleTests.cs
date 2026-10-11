using FoodParkDigital.Application.Finance.Commands;
using FoodParkDigital.Application.Finance.DTOs;
using FoodParkDigital.Application.Finance.Queries;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The cuts and the shift history keep who opened, closed and charged by their user (the email they signed in with) and
/// name them: a member of the business in scope by their name, anyone else by their user as before, never both.
/// </summary>
public class CashPeopleTests
{
    private static readonly DateTimeOffset Now = SalesTestFixture.Now;

    [Fact]
    public async Task Drawer_ShouldNameWhoOpenedAndClosedIt_AndKeepTheirUsers()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var mariana = await AddMemberAsync(fixture, tenant.Id, "Mariana", "Ortiz", "mariana@cafe.test");
        var carlos = await AddMemberAsync(fixture, tenant.Id, "Carlos", "Rivas", "carlos@cafe.test");

        var opened = await OpenAsync(fixture, mariana);
        var current = await new GetCurrentCashSessionQueryHandler(fixture.Context, fixture.CurrentUser)
            .Handle(new GetCurrentCashSessionQuery(), CancellationToken.None);
        var closed = await CloseAsync(fixture, opened.Id, carlos);
        var shift = Assert.Single(await HistoryAsync(fixture));

        Assert.Equal(("mariana@cafe.test", "Mariana Ortiz"), (opened.OpenedBy, opened.OpenedByName));
        Assert.Null(opened.ClosedByName);
        Assert.Equal("Mariana Ortiz", current?.OpenedByName);
        Assert.Equal(("Mariana Ortiz", "carlos@cafe.test", "Carlos Rivas"), (closed.OpenedByName, closed.ClosedBy, closed.ClosedByName));
        Assert.Equal(("mariana@cafe.test", "Mariana Ortiz"), (shift.OpenedBy, shift.OpenedByName));
        Assert.Equal(("carlos@cafe.test", "Carlos Rivas"), (shift.ClosedBy, shift.ClosedByName));
    }

    [Fact]
    public async Task DrawerOfNoMember_ShouldKeepItsUser()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        await AddMemberAsync(fixture, tenant.Id, "Mariana", "Ortiz", "mariana@cafe.test");
        // Someone who worked here: their account remains, their membership does not.
        var former = await AddUserAsync(fixture, "Rosa", "Méndez", "rosa@cafe.test");
        var nameless = await AddMemberAsync(fixture, tenant.Id, " ", "", "sinnombre@cafe.test");

        // A drawer from before per-cashier drawers, closed by a request without a user.
        var migrated = new CashSession(tenant.Id, Now.AddDays(-30), "system-migration", 100m);
        migrated.Close(Now.AddDays(-30).AddHours(8), "system", 100m);
        var formers = new CashSession(tenant.Id, Now.AddDays(-2), "rosa@cafe.test", 100m, openedByUserId: former);
        formers.Close(Now.AddDays(-2).AddHours(8), "rosa@cafe.test", 100m);
        var namelessOnes = new CashSession(tenant.Id, Now.AddDays(-1), "sinnombre@cafe.test", 100m, openedByUserId: nameless);
        // The user is matched whatever its case, as Identity matches an email.
        var marianas = new CashSession(tenant.Id, Now, "Mariana@Cafe.TEST", 100m);
        fixture.Context.CashSessions.AddRange(migrated, formers, namelessOnes, marianas);
        await fixture.Context.SaveChangesAsync();

        var history = (await HistoryAsync(fixture)).ToDictionary(s => s.Id);

        Assert.Equal(("system-migration", null, "system", null),
            (history[migrated.Id].OpenedBy, history[migrated.Id].OpenedByName, history[migrated.Id].ClosedBy, history[migrated.Id].ClosedByName));
        Assert.Equal(("rosa@cafe.test", null, null), (history[formers.Id].OpenedBy, history[formers.Id].OpenedByName, history[formers.Id].ClosedByName));
        Assert.Equal(("sinnombre@cafe.test", null), (history[namelessOnes.Id].OpenedBy, history[namelessOnes.Id].OpenedByName));
        Assert.Equal(("Mariana@Cafe.TEST", "Mariana Ortiz"), (history[marianas.Id].OpenedBy, history[marianas.Id].OpenedByName));
    }

    [Fact]
    public async Task UserOfAnotherBusiness_ShouldKeepTheirUser_UnlessTheyAreAMemberHereToo()
    {
        await using var fixture = SalesTestFixture.Create();
        var other = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: "otro");
        var lucia = await AddMemberAsync(fixture, other.Id, "Lucía", "Gómez", "lucia@otro.test");
        // A namesake in the other business does not hide the name of a member of this one.
        await AddMemberAsync(fixture, other.Id, "Mariana", "Ortiz", "mariana@otro.test");
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: "cafe");
        var mariana = await AddMemberAsync(fixture, tenant.Id, "Mariana", "Ortiz", "mariana@cafe.test");
        var pedro = await AddMemberAsync(fixture, tenant.Id, "Pedro", "Luna", "pedro@cafe.test");
        fixture.Context.TenantUsers.Add(new TenantUser(other.Id, pedro, TenantUserRole.Staff));
        var luciasThere = new CashSession(other.Id, Now, "lucia@otro.test", 100m, openedByUserId: lucia);
        // Records here signed by Lucía, with her id or only her user: she is not a member of this business.
        var luciasHere = new CashSession(tenant.Id, Now.AddHours(-3), "lucia@otro.test", 100m, openedByUserId: lucia);
        luciasHere.Close(Now.AddHours(-1), "lucia@otro.test", 100m);
        var pedros = new CashSession(tenant.Id, Now.AddHours(-2), "pedro@cafe.test", 100m, openedByUserId: pedro);
        var marianas = new CashSession(tenant.Id, Now.AddHours(-1), "mariana@cafe.test", 100m, openedByUserId: mariana);
        fixture.Context.CashSessions.AddRange(luciasThere, luciasHere, pedros, marianas);
        await fixture.Context.SaveChangesAsync();

        var here = (await HistoryAsync(fixture)).ToDictionary(s => s.Id);
        fixture.TenantService.SetTenant(other.Id);
        var there = Assert.Single(await HistoryAsync(fixture));

        Assert.Equal(3, here.Count);
        Assert.Equal(("lucia@otro.test", null, null), (here[luciasHere.Id].OpenedBy, here[luciasHere.Id].OpenedByName, here[luciasHere.Id].ClosedByName));
        Assert.Equal("Pedro Luna", here[pedros.Id].OpenedByName);
        Assert.Equal("Mariana Ortiz", here[marianas.Id].OpenedByName);
        // In her own business she has her name.
        Assert.Equal((luciasThere.Id, "Lucía Gómez"), (there.Id, there.OpenedByName));
    }

    [Fact]
    public async Task MembersWithTheSameName_ShouldKeepTheirUsers()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var juan = await AddMemberAsync(fixture, tenant.Id, "Juan", "Pérez", "juan@cafe.test");
        var otherJuan = await AddMemberAsync(fixture, tenant.Id, "Juan", "Pérez", "juanp@cafe.test");
        fixture.Context.CashSessions.AddRange(
            new CashSession(tenant.Id, Now.AddHours(-1), "juan@cafe.test", 100m, openedByUserId: juan),
            new CashSession(tenant.Id, Now, "juanp@cafe.test", 100m, openedByUserId: otherJuan));
        await fixture.Context.SaveChangesAsync();

        var history = await HistoryAsync(fixture);

        Assert.Equal(["juanp@cafe.test", "juan@cafe.test"], history.Select(s => s.OpenedBy));
        Assert.All(history, shift => Assert.Null(shift.OpenedByName));
    }

    [Fact]
    public async Task DailyCut_ShouldNameEachUser_AndFindThemByTheirName()
    {
        await using var fixture = SalesTestFixture.Create();
        var other = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: "otro");
        await AddMemberAsync(fixture, other.Id, "Lucía", "Gómez", "lucia@otro.test");
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: "cafe");
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 10m);
        var mariana = await AddMemberAsync(fixture, tenant.Id, "Mariana", "Ortiz", "mariana@cafe.test");
        await AddMemberAsync(fixture, tenant.Id, "Juan", "Pérez", "juan@cafe.test");
        await AddMemberAsync(fixture, tenant.Id, "Juan", "Pérez", "juanp@cafe.test");
        var drawer = await OpenAsync(fixture, mariana);
        await ChargeAsync(fixture, soda.Id, 11.60m, "mariana@cafe.test");
        await ChargeAsync(fixture, soda.Id, 23.20m, "juan@cafe.test");
        await ChargeAsync(fixture, soda.Id, 34.80m, "juanp@cafe.test");
        await ChargeAsync(fixture, soda.Id, 46.40m, "lucia@otro.test");
        await ChargeAsync(fixture, soda.Id, 58.00m, createdBy: null);
        await DepositAsync(fixture, drawer.Id, 5m, "mariana@cafe.test");

        var cut = await CutAsync(fixture);
        var byName = await CutAsync(fixture, "  mariana ORTIZ ");
        var byUser = await CutAsync(fixture, "mariana@cafe.test");
        var namesakes = await CutAsync(fixture, "Juan Pérez");
        var outsider = await CutAsync(fixture, "Lucía Gómez");

        Assert.Equal(
            [
                ("Sistema", null),
                ("lucia@otro.test", null),
                ("juanp@cafe.test", null),
                ("juan@cafe.test", null),
                ("mariana@cafe.test", "Mariana Ortiz")
            ],
            cut.ByUser.Select(u => (u.UserName, u.DisplayName)));
        foreach (var mine in new[] { byName, byUser })
        {
            var only = Assert.Single(mine.ByUser);
            Assert.Equal(("mariana@cafe.test", "Mariana Ortiz", 1, 11.60m), (only.UserName, only.DisplayName, only.PaymentsCount, only.TotalAmount));
            Assert.Equal(5m, mine.ManualIncomeTotal);
        }
        // A name two members share finds no one: their users do, and the cut shows them.
        Assert.Equal((0, 0m), (namesakes.SalesPaymentsCount, namesakes.ManualIncomeTotal));
        Assert.Equal(0, outsider.SalesPaymentsCount);
    }

    private static async Task<Guid> AddMemberAsync(SalesTestFixture fixture, Guid tenantId, string firstName, string lastName, string email)
    {
        var userId = await AddUserAsync(fixture, firstName, lastName, email);
        fixture.Context.TenantUsers.Add(new TenantUser(tenantId, userId, TenantUserRole.Staff));
        await fixture.Context.SaveChangesAsync();
        return userId;
    }

    private static async Task<Guid> AddUserAsync(SalesTestFixture fixture, string firstName, string lastName, string email)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            FirstName = firstName,
            LastName = lastName,
            Role = UserRole.Staff,
            IsActive = true
        };
        fixture.Context.Users.Add(user);
        await fixture.Context.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<CashSessionDto> OpenAsync(SalesTestFixture fixture, Guid userId)
    {
        await SignInAsync(fixture, userId);
        var opened = await new OpenCashSessionCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock)
            .Handle(new OpenCashSessionCommand { OpeningAmount = 500m }, CancellationToken.None);
        Assert.True(opened.IsSuccess, opened.Error);
        return opened.Value;
    }

    private static async Task<CashSessionDto> CloseAsync(SalesTestFixture fixture, Guid sessionId, Guid userId)
    {
        await SignInAsync(fixture, userId);
        fixture.Context.ChangeTracker.Clear();
        var closed = await new CloseCashSessionCommandHandler(fixture.Context, fixture.CurrentUser, TestAccess.For(fixture.CurrentUser, fixture.TenantService), fixture.Clock)
            .Handle(new CloseCashSessionCommand { CashSessionId = sessionId, ClosingAmount = 500m }, CancellationToken.None);
        Assert.True(closed.IsSuccess, closed.Error);
        return closed.Value;
    }

    /// <summary>The request is of this user: their id and the email of their token.</summary>
    private static async Task SignInAsync(SalesTestFixture fixture, Guid userId)
    {
        fixture.CurrentUser.UserId = userId.ToString();
        fixture.CurrentUser.UserName = await fixture.Context.Users.Where(u => u.Id == userId).Select(u => u.Email).SingleAsync();
    }

    private static async Task<List<CashSessionDto>> HistoryAsync(SalesTestFixture fixture)
        => (await new GetCashSessionsQueryHandler(fixture.Context, fixture.CurrentUser)
            .Handle(new GetCashSessionsQuery(), CancellationToken.None)).Items.ToList();

    private static Task<DailyCashCutDto> CutAsync(SalesTestFixture fixture, string? userName = null)
        => new GetDailyCashCutQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetDailyCashCutQuery { UserName = userName }, CancellationToken.None);

    /// <summary>A cash sale whose payment <paramref name="createdBy"/> took (as the audit interceptor stores it).</summary>
    private static async Task ChargeAsync(SalesTestFixture fixture, Guid productId, decimal amount, string? createdBy)
    {
        var sale = await fixture.CreateOrderHandler().Handle(new CreateOrderCommand
        {
            Items = [new CreateOrderItemDto { ProductId = productId, Quantity = (int)(amount / 11.60m) }],
            Payments = [new CreateOrderPaymentDto { Amount = amount, Method = OrderPaymentMethod.Cash }]
        }, CancellationToken.None);
        Assert.True(sale.IsSuccess, sale.Error);
        var payment = await fixture.Context.OrderPayments.SingleAsync(p => p.OrderId == sale.Value.Id);
        payment.CreatedBy = createdBy;
        await fixture.Context.SaveChangesAsync();
    }

    private static async Task DepositAsync(SalesTestFixture fixture, Guid sessionId, decimal amount, string createdBy)
    {
        var drawer = await fixture.Context.CashSessions.Include(s => s.Transactions).SingleAsync(s => s.Id == sessionId);
        var deposit = drawer.RegisterTransaction(CashTransactionType.Deposit, amount, Now);
        deposit.CreatedBy = createdBy;
        await fixture.Context.SaveChangesAsync();
    }
}
