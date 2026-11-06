using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// AddTenantUserQuoteReplyEmails (Fase 29O, ADR 0069): whether a member gets an email with each answer to a quote from its
/// link. The members that exist have not chosen (the owners get them), the version before it keeps adding members that have
/// not chosen either, and rolling back drops only the column, never a member nor what they chose for the agenda's notices.
/// </summary>
public class TenantUserQuoteReplyEmailsMigrationSqlServerTests
{
    // The migration right before AddTenantUserQuoteReplyEmails, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261105171726_AddTenantUserRequestEmails";
    private const string Migration = "20261106060148_AddTenantUserQuoteReplyEmails";

    [SqlServerFact]
    public async Task TheMembersThatExist_ShouldNotHaveChosen_SoOnlyTheOwnersGetTheEmails()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var tenant = Business();
        var owner = Account("dueña");
        var admin = Account("admin");
        await using (var before = BeforeTenantUserQuoteReplyEmails.CreateContext(database))
        {
            before.AddRange(tenant, owner, admin);
            before.TenantUsers.AddRange(
                new TenantUser(tenant.Id, owner.Id, TenantUserRole.Owner),
                new TenantUser(tenant.Id, admin.Id, TenantUserRole.Admin));
            await before.SaveChangesAsync();
        }

        await MigrateAsync(database, Migration);

        await using var context = database.CreateContext(tenant.Id);
        var members = await context.TenantUsers.AsNoTracking().OrderBy(m => m.Role).ToListAsync();
        Assert.Equal(
            [(TenantUserRole.Owner, (bool?)null, true), (TenantUserRole.Admin, null, false)],
            members.Select(m => (m.Role, m.QuoteReplyEmails, m.GetsQuoteReplyEmails)));
        Assert.Equal("bit|1|", await ColumnAsync(context));

        // What each one chooses from now on is kept.
        var tracked = await context.TenantUsers.OrderBy(m => m.Role).ToListAsync();
        tracked[0].ChooseQuoteReplyEmails(false);
        tracked[1].ChooseQuoteReplyEmails(true);
        await context.SaveChangesAsync();
        await using var check = database.CreateContext(tenant.Id);
        Assert.Equal(
            [(bool?)false, true],
            await check.TenantUsers.AsNoTracking().OrderBy(m => m.Role).Select(m => m.QuoteReplyEmails).ToListAsync());
    }

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepAddingMembers_ThatHaveNotChosen()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);

        // The app as deployed before this version registers a business with its owner, unaware of the column.
        var tenant = Business();
        var owner = Account("dueña");
        await using (var before = BeforeTenantUserQuoteReplyEmails.CreateContext(database))
        {
            before.AddRange(tenant, owner, new TenantUser(tenant.Id, owner.Id, TenantUserRole.Owner));
            await before.SaveChangesAsync();
        }

        await using var context = database.CreateContext(tenant.Id);
        var member = await context.TenantUsers.AsNoTracking().SingleAsync();
        Assert.Equal(((bool?)null, true), (member.QuoteReplyEmails, member.GetsQuoteReplyEmails));
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldDropOnlyTheColumn_AndForwardAgainTheMembersHaveNotChosen()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = Business();
        var owner = Account("dueña");
        await using (var seed = database.CreateContext(tenant.Id))
        {
            var member = new TenantUser(tenant.Id, owner.Id, TenantUserRole.Owner);
            member.ChooseQuoteReplyEmails(false);
            member.ChooseRequestEmails(false);
            seed.AddRange(tenant, owner, member);
            await seed.SaveChangesAsync();
        }

        await MigrateAsync(database, BeforeMigration);
        await using (var context = BeforeTenantUserQuoteReplyEmails.CreateContext(database, tenant.Id))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Null(await context.Database.SqlQuery<int?>($"SELECT COL_LENGTH(N'dbo.TenantUsers', N'QuoteReplyEmails') AS [Value]").SingleAsync());
            // The member stays as it was, with what they chose for the agenda's notices.
            var member = await context.TenantUsers.AsNoTracking().SingleAsync();
            Assert.Equal((owner.Id, TenantUserRole.Owner, TenantUserStatus.Active, (bool?)false),
                (member.UserId, member.Role, member.Status, member.RequestEmails));
        }

        // Rolling back again does nothing; forward again, what was chosen for the quotes is gone and the owner gets them again.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(tenant.Id);
        var again = await forward.TenantUsers.AsNoTracking().SingleAsync();
        Assert.Equal(((bool?)null, true), (again.QuoteReplyEmails, again.GetsQuoteReplyEmails));
    }

    private static Tenant Business()
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        return new Tenant($"Estética {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
    }

    private static ApplicationUser Account(string name)
    {
        var email = $"{name}.{Guid.NewGuid():N}@demo.com";
        return new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            FirstName = name,
            LastName = "Equipo",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Role = UserRole.Staff,
            IsActive = true
        };
    }

    /// <summary>The column's type, whether it takes NULL (1) and its default ('' for none).</summary>
    private static Task<string> ColumnAsync(ApplicationDbContext context)
        => context.Database.SqlQuery<string>($"""
            SELECT CONCAT(TYPE_NAME(c.[user_type_id]), N'|', c.[is_nullable], N'|', OBJECT_DEFINITION(c.[default_object_id])) AS [Value]
            FROM sys.columns c
            WHERE c.[object_id] = OBJECT_ID(N'dbo.TenantUsers') AND c.[name] = N'QuoteReplyEmails'
            """).SingleAsync();

    private static async Task MigrateAsync(SqlServerTestDatabase database, string target)
    {
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static Task<string> LatestMigrationAsync(ApplicationDbContext context) =>
        context.Database.SqlQuery<string>($"""
            SELECT TOP (1) [MigrationId] AS [Value] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC
            """).SingleAsync();
}
