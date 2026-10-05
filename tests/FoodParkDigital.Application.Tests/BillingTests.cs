using FoodParkDigital.Application.Billing;
using FoodParkDigital.Application.Branches;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Tenants.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Billing.Entities;
using FoodParkDigital.Domain.Billing.Enums;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Application.Tests;

/// <summary>Plans, subscriptions and what they allow: modules, branches, users and access.</summary>
public class BillingTests
{
    [Fact]
    public async Task AssigningAPlan_ShouldStartTheTrial_AndKeepOnlyTheModulesItIncludes()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food | TenantCapability.Kitchen | TenantCapability.Inventory);
        var plan = await AddPlanAsync(fixture, "BASICO", TenantCapability.Food | TenantCapability.Inventory, trialDays: 14);

        var result = await Assign(fixture).Handle(new AssignPlanCommand { TenantId = tenant.Id, PlanId = plan.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var subscription = await fixture.Context.TenantSubscriptions.SingleAsync();
        Assert.Equal(SubscriptionStatus.Trial, subscription.Status);
        Assert.Equal(SalesTestFixture.Now.AddDays(14), subscription.TrialEndsAt);
        var settings = await fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id);
        Assert.Equal(TenantCapability.Food | TenantCapability.Inventory, settings.Capabilities);
        Assert.Contains(await fixture.Context.AuditLogs.ToListAsync(), a => a.Action == "PlanAssigned" && a.TenantId == tenant.Id);
    }

    [Fact]
    public async Task Payment_ShouldActivateTheBusiness_ForThePlansPriceTimesTheMonths()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var plan = await AddPlanAsync(fixture, "PRO", TenantCapability.Retail, trialDays: 0, price: 349m);
        await Assign(fixture).Handle(new AssignPlanCommand { TenantId = tenant.Id, PlanId = plan.Id }, CancellationToken.None);

        var paid = await Payments(fixture).Handle(new RegisterSubscriptionPaymentCommand
        {
            TenantId = tenant.Id,
            Months = 3,
            Method = SubscriptionPaymentMethod.Transfer,
            Reference = "SPEI 4471"
        }, CancellationToken.None);

        Assert.True(paid.IsSuccess, paid.Error);
        var subscription = await fixture.Context.TenantSubscriptions.Include(s => s.Payments).SingleAsync();
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(1047m, Assert.Single(subscription.Payments).Amount);
        Assert.Equal(SalesTestFixture.Now.AddMonths(3), subscription.PaidThrough);
        Assert.Equal(TenantStatus.Active, (await fixture.Context.Tenants.SingleAsync(t => t.Id == tenant.Id)).Status);
    }

    [Fact]
    public async Task UnpaidBusiness_ShouldBeSuspendedAfterTheGraceDays_AndComeBackWhenItPays()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var plan = await AddPlanAsync(fixture, "PRO", TenantCapability.Retail, trialDays: 7);
        await Assign(fixture).Handle(new AssignPlanCommand { TenantId = tenant.Id, PlanId = plan.Id }, CancellationToken.None);
        var later = SalesTestFixture.Now.AddDays(7 + TenantSubscription.GraceDays + 1);

        var stale = await SubscriptionStatusRefresher.FindStaleTenantsAsync(fixture.Context, later, CancellationToken.None);
        var changed = await SubscriptionStatusRefresher.RefreshAsync(fixture.Context, tenant.Id, later, CancellationToken.None);
        var again = await SubscriptionStatusRefresher.RefreshAsync(fixture.Context, tenant.Id, later, CancellationToken.None);

        Assert.Equal([tenant.Id], stale);
        Assert.True(changed);
        Assert.False(again);
        Assert.Equal(TenantStatus.Suspended, (await fixture.Context.Tenants.SingleAsync(t => t.Id == tenant.Id)).Status);

        fixture.Clock.UtcNow = later;
        var paid = await Payments(fixture).Handle(new RegisterSubscriptionPaymentCommand { TenantId = tenant.Id, Months = 1 }, CancellationToken.None);
        Assert.True(paid.IsSuccess, paid.Error);
        Assert.Equal(TenantStatus.Active, (await fixture.Context.Tenants.SingleAsync(t => t.Id == tenant.Id)).Status);
    }

    [Fact]
    public async Task SuspendingAndCancelling_ShouldTakeAwayTheAccessOfTheBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var plan = await AddPlanAsync(fixture, "PRO", TenantCapability.Retail, trialDays: 30);
        await Assign(fixture).Handle(new AssignPlanCommand { TenantId = tenant.Id, PlanId = plan.Id }, CancellationToken.None);

        var suspended = await new SuspendTenantCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock)
            .Handle(new SuspendTenantCommand { TenantId = tenant.Id, Reason = "Contracargo" }, CancellationToken.None);
        var statusWhileSuspended = (await fixture.Context.Tenants.SingleAsync(t => t.Id == tenant.Id)).Status;
        var reactivated = await new ReactivateTenantCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock)
            .Handle(new ReactivateTenantCommand { TenantId = tenant.Id }, CancellationToken.None);
        var statusAfterReactivating = (await fixture.Context.Tenants.SingleAsync(t => t.Id == tenant.Id)).Status;
        var cancelled = await new CancelSubscriptionCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock)
            .Handle(new CancelSubscriptionCommand { TenantId = tenant.Id, Reason = "Cerró" }, CancellationToken.None);

        Assert.True(suspended.IsSuccess && reactivated.IsSuccess && cancelled.IsSuccess);
        Assert.Equal(TenantStatus.Suspended, statusWhileSuspended);
        Assert.Equal(TenantStatus.Active, statusAfterReactivating);
        Assert.Equal(TenantStatus.Inactive, (await fixture.Context.Tenants.SingleAsync(t => t.Id == tenant.Id)).Status);
    }

    [Fact]
    public async Task Owner_ShouldNotTurnOnModulesOutsideThePlan()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var plan = await AddPlanAsync(fixture, "BASICO", TenantCapability.Retail, trialDays: 14);
        await Assign(fixture).Handle(new AssignPlanCommand { TenantId = tenant.Id, PlanId = plan.Id }, CancellationToken.None);
        fixture.TenantService.SetTenant(tenant.Id);

        var result = await new UpdateMyTenantCommandHandler(fixture.Context, fixture.CurrentUser, fixture.TenantService)
            .Handle(new UpdateMyTenantCommand
            {
                Name = tenant.Name,
                ContactEmail = tenant.ContactEmail!,
                Capabilities = TenantCapability.Retail | TenantCapability.Inventory
            }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("inventario", result.Error);
    }

    [Fact]
    public async Task BusinessWithoutPlan_ShouldNotBeLimited()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);

        Assert.Null(await SubscriptionRules.CheckModulesAsync(fixture.Context, tenant.Id, TenantCapability.Retail | TenantCapability.Kitchen, CancellationToken.None));
        Assert.Null(await SubscriptionRules.CheckBranchLimitAsync(fixture.Context, tenant.Id, CancellationToken.None));
        Assert.Null(await SubscriptionRules.CheckUserLimitAsync(fixture.Context, tenant.Id, CancellationToken.None));
    }

    [Fact]
    public async Task NewBranch_ShouldBeRefused_WhenThePlanIsFull()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var tenantId = await SeedTenantAsync(host);
        await host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var plan = new Plan("UNA", "Una sucursal", null, 199m, TenantCapability.Retail, maxBranches: 1, maxUsers: 3, trialDays: 14, sortOrder: 0);
            db.Plans.Add(plan);
            db.TenantSubscriptions.Add(new TenantSubscription(tenantId, plan.Id, 14, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        });

        var result = await host.HandleAsync<CreateBranchCommandHandler, Result<BranchDto>>(
            handler => handler.Handle(new CreateBranchCommand { Name = "Norte", Code = "NTE" }, CancellationToken.None));

        Assert.False(result.IsSuccess);
        Assert.Contains("1 sucursal activa", result.Error);
    }

    [Fact]
    public async Task Plans_ShouldOfferOneToNewBusinesses_AndTrimTheModulesOfTheirBusinessesWhenTheyShrink()
    {
        await using var host = await SqliteTestHost.CreateAsync("SUPER_ADMIN");
        var tenantId = await SeedTenantAsync(host, TenantCapability.Retail | TenantCapability.Inventory);
        var basic = await CreatePlanAsync(host, new CreatePlanCommand
        {
            Code = "basico", Name = "Básico", MonthlyPrice = 199m, Modules = TenantCapability.Retail | TenantCapability.Inventory,
            MaxBranches = 1, MaxUsers = 3, TrialDays = 14, IsDefaultForNewBusinesses = true
        });
        var pro = await CreatePlanAsync(host, new CreatePlanCommand
        {
            Code = "pro", Name = "Pro", MonthlyPrice = 499m, Modules = TenantCapability.Retail | TenantCapability.Inventory | TenantCapability.Appointments,
            MaxBranches = 3, MaxUsers = 10, TrialDays = 14, IsDefaultForNewBusinesses = true
        });
        await host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            db.TenantSubscriptions.Add(new TenantSubscription(tenantId, basic.Id, 14, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        });

        var updated = await host.HandleAsync<UpdatePlanCommandHandler, Result<PlanDto>>(handler => handler.Handle(new UpdatePlanCommand
        {
            Id = basic.Id, Code = "BASICO", Name = "Básico", MonthlyPrice = 199m, Modules = TenantCapability.Retail,
            MaxBranches = 1, MaxUsers = 3, TrialDays = 14
        }, CancellationToken.None));

        Assert.True(updated.IsSuccess, updated.Error);
        var plans = await host.QueryAsync(db => db.Plans.ToListAsync());
        Assert.Equal(pro.Id, Assert.Single(plans, p => p.IsDefaultForNewBusinesses).Id);
        var settings = await host.QueryAsync(db => db.TenantSettings.SingleAsync(s => s.TenantId == tenantId));
        Assert.Equal(TenantCapability.Retail, settings.Capabilities);
        Assert.Contains(await host.QueryAsync(db => db.AuditLogs.IgnoreQueryFilters().ToListAsync()), a => a.Action == "PlanModulesTrimmed");
    }

    [Fact]
    public async Task PlatformTenants_ShouldListThePlanAndTheBillingStatusOfEachBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        var withPlan = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: "con-plan");
        var withoutPlan = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: "sin-plan");
        var plan = await AddPlanAsync(fixture, "PRO", TenantCapability.Retail, trialDays: 0, price: 300m);
        await Assign(fixture).Handle(new AssignPlanCommand { TenantId = withPlan.Id, PlanId = plan.Id }, CancellationToken.None);
        await Payments(fixture).Handle(new RegisterSubscriptionPaymentCommand { TenantId = withPlan.Id, Months = 1 }, CancellationToken.None);

        var tenants = await new GetPlatformTenantsQueryHandler(fixture.Context).Handle(new GetPlatformTenantsQuery(), CancellationToken.None);
        var onlyWithoutPlan = await new GetPlatformTenantsQueryHandler(fixture.Context).Handle(new GetPlatformTenantsQuery { WithoutPlan = true }, CancellationToken.None);
        var overview = await new GetPlatformOverviewQueryHandler(fixture.Context, fixture.Clock).Handle(new GetPlatformOverviewQuery(), CancellationToken.None);

        var row = Assert.Single(tenants.Items, t => t.TenantId == withPlan.Id);
        Assert.Equal("Plan PRO", row.PlanName);
        Assert.Equal(SubscriptionStatus.Active, row.SubscriptionStatus);
        Assert.Equal(1, row.BranchesCount);
        Assert.Equal(withoutPlan.Id, Assert.Single(onlyWithoutPlan.Items).TenantId);
        Assert.Equal(1, overview.Paying);
        Assert.Equal(1, overview.WithoutPlan);
        Assert.Equal(300m, overview.MonthlyRecurringRevenue);
        Assert.Equal(300m, overview.PaymentsLast30Days);
    }

    private static async Task<Plan> AddPlanAsync(SalesTestFixture fixture, string code, TenantCapability modules, int trialDays, decimal price = 199m)
    {
        var plan = new Plan(code, $"Plan {code}", null, price, modules, maxBranches: 3, maxUsers: 5, trialDays, sortOrder: 0);
        fixture.Context.Plans.Add(plan);
        await fixture.Context.SaveChangesAsync();
        return plan;
    }

    private static AssignPlanCommandHandler Assign(SalesTestFixture fixture)
        => new(fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock);

    private static RegisterSubscriptionPaymentCommandHandler Payments(SalesTestFixture fixture)
        => new(fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock);

    private static async Task<Guid> SeedTenantAsync(SqliteTestHost host, TenantCapability capabilities = TenantCapability.Retail)
    {
        var tenant = new Tenant("Café Luna", "cafe-luna", BusinessType.Retail, "luna@demo.com");
        host.SetTenant(tenant.Id);
        await host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            db.Tenants.Add(tenant);
            db.TenantSettings.Add(new TenantSettings(tenant.Id, capabilities, pricesIncludeTax: true));
            db.Branches.Add(Branch.CreateDefault(tenant.Id));
            await db.SaveChangesAsync();
        });
        return tenant.Id;
    }

    private static async Task<PlanDto> CreatePlanAsync(SqliteTestHost host, CreatePlanCommand command)
    {
        var result = await host.HandleAsync<CreatePlanCommandHandler, Result<PlanDto>>(handler => handler.Handle(command, CancellationToken.None));
        Assert.True(result.IsSuccess, result.Error);
        return result.Value;
    }
}
