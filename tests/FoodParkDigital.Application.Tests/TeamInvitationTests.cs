using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Employees;
using FoodParkDigital.Application.Employees.Commands;
using FoodParkDigital.Application.Employees.DTOs;
using FoodParkDigital.Application.Identity.Commands;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Application.Identity.Services;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The team is invited, never given a password someone else invented: the person opens a link (by email or shared by
/// WhatsApp), chooses their password and enters. Accounts are shared by the businesses of a person.
/// </summary>
public class TeamInvitationTests
{
    private const string Password = "Secreto1";

    [Fact]
    public async Task Invitation_ShouldLetThePersonChooseTheirPassword_AndEnter()
    {
        await using var host = await CreateHostAsync();
        var tenant = await SeedTenantAsync(host, "Tacos Don Pepe");

        var invited = await InviteAsync(host, tenant, "ana@demo.com", permissions: null);
        Assert.True(invited.IsSuccess, invited.Error);
        var employee = invited.Value!;
        Assert.Equal("Invited", employee.Status);
        Assert.False(employee.IsActive);
        Assert.StartsWith("/aceptar-invitacion?userId=", employee.InvitationUrl);
        var token = TokenOf(employee.InvitationUrl!);
        // The email goes out from the outbox with a link of its own; the one the admin shares stays valid as well.
        Assert.Empty(host.Email.Invitations);
        await host.DispatchAccountEmailsAsync();
        var emailed = Assert.Single(host.Email.Invitations);
        Assert.Equal(("ana@demo.com", "Tacos Don Pepe"), (emailed.Email, emailed.Business));
        Assert.StartsWith($"/aceptar-invitacion?userId={employee.UserId}&token=", emailed.Path);
        Assert.True((await GetInvitationAsync(host, employee.UserId, TokenOf(emailed.Path))).IsSuccess);

        var shown = await GetInvitationAsync(host, employee.UserId, token);
        Assert.True(shown.IsSuccess, shown.Error);
        Assert.Equal("ana@demo.com", shown.Value!.Email);
        Assert.Equal(["Tacos Don Pepe"], shown.Value.Businesses);

        var accepted = await AcceptAsync(host, employee.UserId, token);
        Assert.True(accepted.IsSuccess, accepted.Error);
        Assert.Equal("session", accepted.Value!.AccessToken);

        var user = await UserAsync(host, employee.UserId);
        Assert.True(user.EmailConfirmed);
        Assert.True(await CheckPasswordAsync(host, employee.UserId, Password));
        Assert.Equal(TenantUserStatus.Active, (await MembershipAsync(host, employee.TenantUserId)).Status);

        // The link works once: the password changed the security stamp.
        var again = await AcceptAsync(host, employee.UserId, token);
        Assert.Equal(EmployeeInvitations.AlreadyAcceptedMessage, again.Error);
    }

    [Fact]
    public async Task InvitationLink_ShouldNotResetAPassword()
    {
        await using var host = await CreateHostAsync();
        var tenant = await SeedTenantAsync(host, "Tienda");
        var employee = (await InviteAsync(host, tenant, "luis@demo.com", permissions: null)).Value!;

        var reset = await host.HandleAsync<ResetPasswordCommandHandler, Result>(handler => handler.Handle(
            new ResetPasswordCommand { UserId = employee.UserId.ToString(), Token = TokenOf(employee.InvitationUrl!), NewPassword = Password },
            CancellationToken.None));

        Assert.False(reset.IsSuccess);
        Assert.False(await CheckPasswordAsync(host, employee.UserId, Password));
    }

    [Fact]
    public async Task CancelledInvitation_ShouldNoLongerOpen_AndActivatingItShouldGiveTheInvitationBack()
    {
        await using var host = await CreateHostAsync();
        var tenant = await SeedTenantAsync(host, "Café");
        var employee = (await InviteAsync(host, tenant, "rosa@demo.com", permissions: null)).Value!;

        var cancelled = await SetActiveAsync(host, tenant, employee.TenantUserId, isActive: false);
        var shown = await GetInvitationAsync(host, employee.UserId, TokenOf(employee.InvitationUrl!));
        var restored = await SetActiveAsync(host, tenant, employee.TenantUserId, isActive: true);

        Assert.True(cancelled.IsSuccess, cancelled.Error);
        Assert.Equal(EmployeeInvitations.InvalidMessage, shown.Error);
        Assert.True(restored.IsSuccess, restored.Error);
        // Without a password, activating them is inviting them again (not an access they cannot use).
        Assert.Equal(TenantUserStatus.Invited, (await MembershipAsync(host, employee.TenantUserId)).Status);
    }

    [Fact]
    public async Task AnInvitationAcceptedBeforeItsEmailGoesOut_ShouldNotBeEmailed()
    {
        await using var host = await CreateHostAsync();
        var tenant = await SeedTenantAsync(host, "Estética Luz");
        var employee = (await InviteAsync(host, tenant, "carla@demo.com", permissions: null)).Value!;

        // The admin shared the link by WhatsApp and the person accepted it before the outbox sent the email.
        Assert.True((await AcceptAsync(host, employee.UserId, TokenOf(employee.InvitationUrl!))).IsSuccess);
        await host.DispatchAccountEmailsAsync();

        Assert.Empty(host.Email.Invitations);
    }

    [Fact]
    public async Task AnInvitedPersonWhoSignsUpOrAsksForALink_ShouldGetTheirInvitation_NotAnActivationLink()
    {
        await using var host = await CreateHostAsync();
        var tenant = await SeedTenantAsync(host, "Tacos Don Pepe");
        var employee = (await InviteAsync(host, tenant, "luis@demo.com", permissions: null)).Value!;
        await host.DispatchAccountEmailsAsync();

        // Activating the email of an account without a password would leave the person unable to sign in.
        host.SetTenant(Guid.Empty);
        Assert.True((await host.HandleAsync<RegisterCustomerCommandHandler, Result<Guid>>(handler => handler.Handle(
            new RegisterCustomerCommand
            {
                Email = "luis@demo.com", Password = Password, FirstName = "Luis", LastName = "Demo", PhoneNumber = "5522223333"
            },
            CancellationToken.None))).IsSuccess);
        Assert.True((await host.HandleAsync<ResendConfirmationEmailCommandHandler, Result>(handler => handler.Handle(
            new ResendConfirmationEmailCommand { Email = "luis@demo.com" }, CancellationToken.None))).IsSuccess);
        await host.DispatchAccountEmailsAsync();

        Assert.Equal(3, host.Email.Invitations.Count);
        Assert.All(host.Email.Invitations, sent => Assert.Equal(("luis@demo.com", "Tacos Don Pepe"), (sent.Email, sent.Business)));
        Assert.True((await AcceptAsync(host, employee.UserId, TokenOf(host.Email.Invitations[^1].Path))).IsSuccess);
        Assert.Empty(host.Email.Confirmations);
        Assert.Empty(host.Email.Unfinished);
    }

    [Fact]
    public async Task Resend_ShouldGiveANewLink_OnlyWhileTheInvitationWaits()
    {
        await using var host = await CreateHostAsync();
        var tenant = await SeedTenantAsync(host, "Taller");
        var employee = (await InviteAsync(host, tenant, "beto@demo.com", permissions: null)).Value!;

        var resent = await ResendAsync(host, tenant, employee.TenantUserId);
        Assert.True(resent.IsSuccess, resent.Error);
        await host.DispatchAccountEmailsAsync();
        Assert.Equal(2, host.Email.Invitations.Count);
        Assert.True((await AcceptAsync(host, employee.UserId, TokenOf(resent.Value!.InvitationUrl))).IsSuccess);

        var afterAccepting = await ResendAsync(host, tenant, employee.TenantUserId);
        Assert.Equal("Esta persona ya no tiene una invitación pendiente.", afterAccepting.Error);
    }

    [Fact]
    public async Task ExistingAccount_ShouldJoinWithItsPassword_AndCustomersShouldBeRefused()
    {
        await using var host = await CreateHostAsync();
        var first = await SeedTenantAsync(host, "Primero");
        var second = await SeedTenantAsync(host, "Segundo");
        var employee = (await InviteAsync(host, first, "maria@demo.com", permissions: null)).Value!;
        await AcceptAsync(host, employee.UserId, TokenOf(employee.InvitationUrl!));
        await SeedCustomerAccountAsync(host, "cliente@demo.com");

        var joined = await InviteAsync(host, second, "MARIA@demo.com", permissions: null, firstName: "Otro nombre");
        var customer = await InviteAsync(host, second, "cliente@demo.com", permissions: null);

        Assert.True(joined.IsSuccess, joined.Error);
        Assert.Equal("Active", joined.Value!.Status);
        Assert.Null(joined.Value.InvitationUrl);
        // The account is theirs: joining another business changes nothing of it.
        Assert.Equal("Maria", (await UserAsync(host, employee.UserId)).FirstName);
        Assert.Equal("Ese correo es de una cuenta de cliente: pídele otro correo para el equipo.", customer.Error);
    }

    [Fact]
    public async Task PersonInvitedByTwoBusinesses_ShouldEnterBoth_WhenAcceptingOne()
    {
        await using var host = await CreateHostAsync();
        var first = await SeedTenantAsync(host, "Primero");
        var second = await SeedTenantAsync(host, "Segundo");
        var fromFirst = (await InviteAsync(host, first, "pablo@demo.com", permissions: null)).Value!;

        var fromSecond = await InviteAsync(host, second, "pablo@demo.com", permissions: null);
        Assert.True(fromSecond.IsSuccess, fromSecond.Error);
        Assert.Equal("Invited", fromSecond.Value!.Status);

        var shown = await GetInvitationAsync(host, fromFirst.UserId, TokenOf(fromSecond.Value.InvitationUrl!));
        Assert.Equal(["Primero", "Segundo"], shown.Value!.Businesses);
        Assert.True((await AcceptAsync(host, fromFirst.UserId, TokenOf(fromFirst.InvitationUrl!))).IsSuccess);

        Assert.Equal(TenantUserStatus.Active, (await MembershipAsync(host, fromFirst.TenantUserId)).Status);
        Assert.Equal(TenantUserStatus.Active, (await MembershipAsync(host, fromSecond.Value.TenantUserId)).Status);
    }

    [Fact]
    public async Task Permissions_ShouldBeTheRoleDefaults_OrExactlyTheChosenOnes_IncludingNone()
    {
        await using var host = await CreateHostAsync();
        var tenant = await SeedTenantAsync(host, "Fonda");

        var defaults = (await InviteAsync(host, tenant, "uno@demo.com", permissions: null)).Value!;
        var waiter = (await InviteAsync(host, tenant, "dos@demo.com", permissions: [nameof(TenantPermission.ManageOrders)])).Value!;
        var noAccess = (await InviteAsync(host, tenant, "tres@demo.com", permissions: [])).Value!;

        Assert.Contains(nameof(TenantPermission.ChargeOrders), defaults.Permissions);
        Assert.Equal([nameof(TenantPermission.ManageOrders)], waiter.Permissions);
        Assert.Empty(noAccess.Permissions);
        Assert.Equal(TenantPermissionCatalog.NoAccessValue, (await MembershipAsync(host, noAccess.TenantUserId)).PermissionCodes);
    }

    [Fact]
    public void RolePresets_ShouldSeparateTakingOrdersFromCharging()
    {
        var presets = EmployeeDefinitions.RolePresets.ToDictionary(p => p.Key);

        Assert.Equal(["manager", "cashier", "waiter", "kitchen", "technician"], EmployeeDefinitions.RolePresets.Select(p => p.Key));
        Assert.Contains(nameof(TenantPermission.ChargeOrders), presets["cashier"].Permissions);
        Assert.Equal([nameof(TenantPermission.ManageOrders)], presets["waiter"].Permissions);
        Assert.Equal(UserRole.Kitchen, presets["kitchen"].AppRole);
        // Whoever manages permissions could give themselves all of them: the team is for owners and admins.
        Assert.DoesNotContain(EmployeeDefinitions.PermissionOptions, o => o.Code == nameof(TenantPermission.ManageEmployees));
    }

    private static Task<SqliteTestHost> CreateHostAsync() => SqliteTestHost.CreateAsync("TENANT_ADMIN", "STAFF", "KITCHEN", "CUSTOMER");

    private static Task<Tenant> SeedTenantAsync(SqliteTestHost host, string name)
        => host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var tenant = new Tenant(name, Guid.NewGuid().ToString("N")[..12], BusinessType.Food, $"{Guid.NewGuid():N}@demo.com");
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            return tenant;
        });

    private static Task SeedCustomerAccountAsync(SqliteTestHost host, string email)
        => host.InScopeAsync(async services =>
        {
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, FirstName = "Cliente", LastName = "Demo", Role = UserRole.Customer, IsActive = true };
            Assert.True((await userManager.CreateAsync(user, Password)).Succeeded);
        });

    private static Task<Result<EmployeeDto>> InviteAsync(
        SqliteTestHost host, Tenant tenant, string email, List<string>? permissions, string firstName = "Maria")
    {
        host.SetTenant(tenant.Id);
        return host.InScopeAsync(services => ActivatorUtilities
            .CreateInstance<CreateEmployeeCommandHandler>(services)
            .Handle(new CreateEmployeeCommand
            {
                Email = email,
                FirstName = firstName,
                LastName = "Demo",
                PhoneNumber = "5512345678",
                AppRole = UserRole.Staff,
                TenantRole = TenantUserRole.Staff,
                Permissions = permissions
            }, CancellationToken.None));
    }

    private static Task<Result> SetActiveAsync(SqliteTestHost host, Tenant tenant, Guid tenantUserId, bool isActive)
    {
        host.SetTenant(tenant.Id);
        return host.InScopeAsync(services => ActivatorUtilities
            .CreateInstance<SetEmployeeActiveCommandHandler>(services, Admin(tenant))
            .Handle(new SetEmployeeActiveCommand { TenantUserId = tenantUserId, IsActive = isActive }, CancellationToken.None));
    }

    private static Task<Result<EmployeeInvitationDto>> ResendAsync(SqliteTestHost host, Tenant tenant, Guid tenantUserId)
    {
        host.SetTenant(tenant.Id);
        return host.HandleAsync<ResendEmployeeInvitationCommandHandler, Result<EmployeeInvitationDto>>(handler =>
            handler.Handle(new ResendEmployeeInvitationCommand(tenantUserId), CancellationToken.None));
    }

    private static Task<Result<InvitationDto>> GetInvitationAsync(SqliteTestHost host, Guid userId, string token)
    {
        host.SetTenant(Guid.Empty);
        return host.HandleAsync<GetInvitationQueryHandler, Result<InvitationDto>>(handler =>
            handler.Handle(new GetInvitationQuery(userId, token), CancellationToken.None));
    }

    private static Task<Result<AuthResponse>> AcceptAsync(SqliteTestHost host, Guid userId, string token)
    {
        host.SetTenant(Guid.Empty);
        return host.InScopeAsync(services => ActivatorUtilities
            .CreateInstance<AcceptInvitationCommandHandler>(services, new SessionIssuer())
            .Handle(new AcceptInvitationCommand { UserId = userId, Token = token, NewPassword = Password }, CancellationToken.None));
    }

    private static string TokenOf(string invitationUrl)
        => Uri.UnescapeDataString(invitationUrl[(invitationUrl.IndexOf("token=", StringComparison.Ordinal) + "token=".Length)..]);

    private static Task<ApplicationUser> UserAsync(SqliteTestHost host, Guid userId)
        => host.QueryAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == userId));

    private static Task<TenantUser> MembershipAsync(SqliteTestHost host, Guid tenantUserId)
        => host.QueryAsync(db => db.TenantUsers.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == tenantUserId));

    private static Task<bool> CheckPasswordAsync(SqliteTestHost host, Guid userId, string password)
        => host.InScopeAsync(async services =>
        {
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId.ToString());
            return await userManager.CheckPasswordAsync(user!, password);
        });

    private static ICurrentUserService Admin(Tenant tenant) => new TenantAdminUser(tenant.Id);

    private sealed class TenantAdminUser(Guid tenantId) : ICurrentUserService
    {
        public string? UserId { get; } = Guid.NewGuid().ToString();
        public string? UserName => "admin@demo.com";
        public string? Role => "TENANT_ADMIN";
        public string? TenantRole => "OWNER";
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId { get; } = tenantId;
    }

    private sealed class SessionIssuer : IAuthSessionIssuer
    {
        public Task<AuthResponse> IssueAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(new AuthResponse { AccessToken = "session", UserId = user.Id.ToString() });
    }
}
