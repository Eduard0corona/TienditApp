using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Tenants.Commands;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

// Uses SQLite because InMemory does not enforce the unique index on PhoneNumberId.
public class UpdateWhatsAppConfigCommandTests
{
    private const string PhoneNumberId = "106540352242922";

    [Fact]
    public async Task Handle_ShouldReject_WhenPhoneNumberIdBelongsToAnotherTenant()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.StoreConfigAsync(fixture.TenantB, PhoneNumberId);

        var result = await fixture.CreateHandler(fixture.TenantA).Handle(Enable(PhoneNumberId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UpdateWhatsAppConfigCommandHandler.PhoneNumberIdInUseError, result.Error);
        Assert.Null(await fixture.FindConfigAsync(fixture.TenantA));
        Assert.Equal(PhoneNumberId, (await fixture.FindConfigAsync(fixture.TenantB))?.PhoneNumberId);
    }

    [Fact]
    public async Task Handle_ShouldReject_WhenPhoneNumberIdOnlyDiffersBySurroundingWhitespace()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.StoreConfigAsync(fixture.TenantB, PhoneNumberId);

        var result = await fixture.CreateHandler(fixture.TenantA)
            .Handle(Enable($"  {PhoneNumberId} "), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UpdateWhatsAppConfigCommandHandler.PhoneNumberIdInUseError, result.Error);
    }

    [Fact]
    public async Task Handle_ShouldAllowTenantToSaveItsOwnPhoneNumberIdAgain()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.StoreConfigAsync(fixture.TenantA, PhoneNumberId);

        var result = await fixture.CreateHandler(fixture.TenantA)
            .Handle(Enable(PhoneNumberId) with { DisplayPhoneNumber = "+52 55 1111 2222" }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(PhoneNumberId, result.Value.PhoneNumberId);
        Assert.Equal("+52 55 1111 2222", result.Value.DisplayPhoneNumber);
    }

    [Fact]
    public async Task Handle_ShouldStoreBlankPhoneNumberIdAsNull_SoUnconfiguredTenantsDoNotCollide()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var disabled = new UpdateWhatsAppConfigCommand { IsEnabled = false, PhoneNumberId = "" };

        var tenantA = await fixture.CreateHandler(fixture.TenantA).Handle(disabled, CancellationToken.None);
        var tenantB = await fixture.CreateHandler(fixture.TenantB).Handle(disabled, CancellationToken.None);

        Assert.True(tenantA.IsSuccess, tenantA.Error);
        Assert.True(tenantB.IsSuccess, tenantB.Error);
        Assert.Null((await fixture.FindConfigAsync(fixture.TenantA))?.PhoneNumberId);
        Assert.Null((await fixture.FindConfigAsync(fixture.TenantB))?.PhoneNumberId);
    }

    [Fact]
    public async Task Handle_ShouldReturnClearError_WhenConcurrentClaimWinsUniqueIndex()
    {
        await using var fixture = await TestFixture.CreateAsync();

        // Tenant B claims the number after tenant A's duplicate check but before its insert.
        fixture.Database.BeforeNextSave(() => fixture.StoreConfigAsync(fixture.TenantB, PhoneNumberId));

        var result = await fixture.CreateHandler(fixture.TenantA).Handle(Enable(PhoneNumberId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UpdateWhatsAppConfigCommandHandler.PhoneNumberIdInUseError, result.Error);
        Assert.Null(await fixture.FindConfigAsync(fixture.TenantA));
        Assert.Equal(PhoneNumberId, (await fixture.FindConfigAsync(fixture.TenantB))?.PhoneNumberId);
    }

    private static UpdateWhatsAppConfigCommand Enable(string phoneNumberId) => new()
    {
        IsEnabled = true,
        AccessToken = "test-access-token",
        PhoneNumberId = phoneNumberId,
        WabaId = "test-waba-id"
    };

    private sealed class TestFixture : IAsyncDisposable
    {
        private TestFixture(SqliteTestDatabase database, ApplicationDbContext context, Guid tenantA, Guid tenantB)
        {
            Database = database;
            Context = context;
            TenantA = tenantA;
            TenantB = tenantB;
        }

        public SqliteTestDatabase Database { get; }
        public ApplicationDbContext Context { get; }
        public Guid TenantA { get; }
        public Guid TenantB { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var database = await SqliteTestDatabase.CreateAsync();
            var context = database.CreateContext();

            var tenantA = new Tenant("Tenant A", "tenant-a", BusinessType.Food, "a@tenant.com");
            var tenantB = new Tenant("Tenant B", "tenant-b", BusinessType.Retail, "b@tenant.com");
            context.Tenants.AddRange(tenantA, tenantB);
            await context.SaveChangesAsync();

            return new TestFixture(database, context, tenantA.Id, tenantB.Id);
        }

        /// <summary>The handler of a request of <paramref name="tenantId"/>: the filters of the context are its own.</summary>
        public UpdateWhatsAppConfigCommandHandler CreateHandler(Guid tenantId)
        {
            Database.SetTenant(tenantId);
            return new(Context, new TestCurrentTenantService(tenantId));
        }

        public async Task StoreConfigAsync(Guid tenantId, string phoneNumberId)
        {
            await using var otherContext = Database.CreateIndependentContext();
            var config = new WhatsAppConfig(tenantId);
            config.Update(true, "other-access-token", phoneNumberId, "other-waba-id", null, Guid.NewGuid().ToString("N"));
            otherContext.WhatsAppConfigs.Add(config);
            await otherContext.SaveChangesAsync();
        }

        public async Task<WhatsAppConfig?> FindConfigAsync(Guid tenantId)
        {
            // What any business has stored, read by the test whatever business is in context.
            await using var readContext = Database.CreateIndependentContext();
            return await readContext.WhatsAppConfigs
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleOrDefaultAsync(c => c.TenantId == tenantId);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Database.DisposeAsync();
        }
    }

    private sealed class TestCurrentTenantService : ICurrentTenantService
    {
        public TestCurrentTenantService(Guid tenantId)
        {
            TenantId = tenantId;
        }

        public Guid TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }
}
