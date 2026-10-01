using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Tests;

public class CustomerLookupServiceLoggingTests
{
    private const string RawPhone = "55 1234 5678";
    private const string PhoneNumber = "+525512345678";
    private const string NationalNumber = "5512345678";
    private const string MaskedPhoneNumber = "***5678";

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task ResolveForOrderAsync_WhenPhoneBelongsToCustomer_LogsOnlyMaskedPhoneOutsideDevelopment(string environmentName)
    {
        await using var context = InMemoryApplicationDbContext.Create();
        var userId = await SeedCustomerAsync(context);
        var logger = new CapturingLogger<CustomerLookupService>();
        var service = CreateService(environmentName, context, logger);

        var result = await service.ResolveForOrderAsync(Guid.NewGuid(), RawPhone, null, "MX", CancellationToken.None);

        Assert.Equal(userId, result.CustomerUserId);
        Assert.Null(logger.FirstMentioning(NationalNumber));
        var linked = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, linked.Level);
        Assert.Equal(MaskedPhoneNumber, linked["Phone"]);
        Assert.Equal(userId, linked["UserId"]);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task ResolveForOrderAsync_WhenCreatingTemporaryCustomer_LogsOnlyMaskedPhoneOutsideDevelopment(string environmentName)
    {
        await using var context = InMemoryApplicationDbContext.Create();
        var tenantId = Guid.NewGuid();
        var logger = new CapturingLogger<CustomerLookupService>();
        var service = CreateService(environmentName, context, logger);

        var result = await service.ResolveForOrderAsync(tenantId, RawPhone, "Cliente", "MX", CancellationToken.None);

        Assert.NotNull(result.TemporaryCustomerId);
        Assert.Null(logger.FirstMentioning(NationalNumber));
        var created = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, created.Level);
        Assert.Equal(MaskedPhoneNumber, created["Phone"]);
        Assert.Equal(tenantId, created["TenantId"]);
        Assert.Equal(result.TemporaryCustomerId, created["TemporaryCustomerId"]);
    }

    [Fact]
    public async Task ResolveForOrderAsync_InDevelopment_LogsFullPhoneForDebugging()
    {
        await using var context = InMemoryApplicationDbContext.Create();
        await SeedCustomerAsync(context);
        var logger = new CapturingLogger<CustomerLookupService>();
        var service = CreateService(Environments.Development, context, logger);

        await service.ResolveForOrderAsync(Guid.NewGuid(), RawPhone, null, "MX", CancellationToken.None);
        await service.ResolveForOrderAsync(Guid.NewGuid(), "55 8765 4321", null, "MX", CancellationToken.None);

        Assert.Collection(
            logger.Entries,
            linked => Assert.Equal(PhoneNumber, linked["Phone"]),
            created => Assert.Equal("+525587654321", created["Phone"]));
    }

    // A registered customer who confirmed this number by SMS.
    private static async Task<Guid> SeedCustomerAsync(ApplicationDbContext context)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "cliente@example.com",
            Email = "cliente@example.com",
            FirstName = "Ana",
            LastName = "Cliente",
            Role = UserRole.Customer,
            PhoneNumber = PhoneNumber,
            PhoneNumberConfirmed = true
        };
        context.Users.Add(user);
        context.CustomerProfiles.Add(new CustomerProfile(user.Id, PhoneNumber, "Ana", "Ana", "Cliente"));
        await context.SaveChangesAsync();
        return user.Id;
    }

    private static CustomerLookupService CreateService(
        string environmentName,
        ApplicationDbContext context,
        CapturingLogger<CustomerLookupService> logger)
        => new(context, new PhoneNormalizationService(), new TestHostEnvironment(environmentName), logger);
}
