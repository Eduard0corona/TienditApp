using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Infrastructure.Persistence.Configurations;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The platform admin keeps the 0 it always had in the database (the rows already saved and the version being
/// replaced read the same), every other role its number, and an account without a role is never saved.
/// </summary>
public class UserRoleStorageTests
{
    [Theory]
    [InlineData(UserRole.SuperAdmin, 0)]
    [InlineData(UserRole.TenantAdmin, 1)]
    [InlineData(UserRole.Staff, 2)]
    [InlineData(UserRole.Kitchen, 3)]
    [InlineData(UserRole.Customer, 4)]
    public void Role_ShouldBeStoredWithTheNumberItAlwaysHad(UserRole role, int stored)
    {
        Assert.Equal(stored, UserRoleStorage.ToStored(role));
        Assert.Equal(role, UserRoleStorage.FromStored(stored));
    }

    [Fact]
    public void AccountWithoutARole_ShouldNotBeSaved()
    {
        Assert.Throws<InvalidOperationException>(() => UserRoleStorage.ToStored(UserRole.None));
    }

    [SqlServerFact]
    public async Task RowsOfThePreviousVersion_ShouldReadTheSame_AndANewAccountWithoutRoleShouldFail()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var admin = User("admin@demo.com", UserRole.SuperAdmin);
        var owner = User("dueno@demo.com", UserRole.TenantAdmin);
        await using (var context = database.CreateContext())
        {
            context.Users.AddRange(admin, owner);
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            // What is in the table: the platform admin as 0, as the previous version wrote and reads it.
            var stored = await context.Database
                .SqlQuery<int>($"SELECT Role AS [Value] FROM AspNetUsers WHERE Id = {admin.Id}")
                .SingleAsync();
            Assert.Equal(0, stored);
            Assert.Equal(UserRole.SuperAdmin, (await context.Users.SingleAsync(u => u.Id == admin.Id)).Role);
            Assert.Equal(admin.Id, await context.Users.Where(u => u.Role == UserRole.SuperAdmin).Select(u => u.Id).SingleAsync());
            Assert.Equal(UserRole.TenantAdmin, (await context.Users.SingleAsync(u => u.Id == owner.Id)).Role);

            context.Users.Add(User("sinrol@demo.com", UserRole.None));
            var refused = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.IsType<InvalidOperationException>(refused.InnerException);
        }

        await using (var context = database.CreateContext())
            Assert.Equal(2, await context.Users.CountAsync());
    }

    private static ApplicationUser User(string email, UserRole role) => new()
    {
        Id = Guid.NewGuid(),
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        FirstName = "Cuenta",
        LastName = "Demo",
        SecurityStamp = Guid.NewGuid().ToString("N"),
        Role = role
    };
}
