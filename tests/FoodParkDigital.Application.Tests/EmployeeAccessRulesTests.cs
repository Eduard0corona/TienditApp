using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Employees;
using FoodParkDigital.Application.Employees.Commands;
using FoodParkDigital.Application.Employees.DTOs;
using FoodParkDigital.Application.Employees.Queries;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Domain.Tenants.Events;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The owner keeps the business, and someone always administers it, whatever the team edits. Since the Fase 29Q (ADR 0071),
/// whoever runs the team also chooses the notices by email of each person, but never the owner's.
/// </summary>
public class EmployeeAccessRulesTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    [Fact]
    public async Task Owner_ShouldKeepTheirRole_WhenAnAdminEditsThem()
    {
        await using var fixture = await Fixture.CreateAsync();
        var owner = await fixture.AddMemberAsync(TenantA, UserRole.TenantAdmin, TenantUserRole.Owner);
        var admin = await fixture.AddMemberAsync(TenantA, UserRole.TenantAdmin, TenantUserRole.Admin);

        var result = await fixture.UpdateAsync(admin, owner, UserRole.Staff, TenantUserRole.Staff, firstName: "Rosa");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Owner", result.Value!.TenantRole);
        Assert.Equal("TenantAdmin", result.Value.AppRole);
        Assert.Equal("Rosa", result.Value.FirstName);
        Assert.Equal(TenantUserRole.Owner, (await fixture.MembershipAsync(owner)).Role);
    }

    [Fact]
    public async Task Owner_ShouldNotBeDeactivated()
    {
        await using var fixture = await Fixture.CreateAsync();
        var owner = await fixture.AddMemberAsync(TenantA, UserRole.TenantAdmin, TenantUserRole.Owner);
        var admin = await fixture.AddMemberAsync(TenantA, UserRole.TenantAdmin, TenantUserRole.Admin);

        var result = await fixture.SetActiveAsync(admin, owner, isActive: false);

        Assert.False(result.IsSuccess);
        Assert.Equal(EmployeeAccessRules.OwnerIsPermanentMessage, result.Error);
        Assert.Equal(TenantUserStatus.Active, (await fixture.MembershipAsync(owner)).Status);
    }

    [Fact]
    public async Task Admin_ShouldNotDemoteThemselves()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddMemberAsync(TenantA, UserRole.TenantAdmin, TenantUserRole.Owner);
        var admin = await fixture.AddMemberAsync(TenantA, UserRole.TenantAdmin, TenantUserRole.Admin);

        var result = await fixture.UpdateAsync(admin, admin, UserRole.Staff, TenantUserRole.Staff);

        Assert.False(result.IsSuccess);
        Assert.Equal(EmployeeAccessRules.SelfDemotionMessage, result.Error);
        Assert.Equal(TenantUserRole.Admin, (await fixture.MembershipAsync(admin)).Role);
    }

    [Fact]
    public async Task LastActiveAdmin_ShouldNotBeDemotedNorDeactivated()
    {
        await using var fixture = await Fixture.CreateAsync();
        // The owner left (disabled) and another business has its own admins: neither counts here.
        var owner = await fixture.AddMemberAsync(TenantA, UserRole.TenantAdmin, TenantUserRole.Owner);
        await fixture.DisableAsync(owner);
        await fixture.AddMemberAsync(TenantB, UserRole.TenantAdmin, TenantUserRole.Admin);
        var admin = await fixture.AddMemberAsync(TenantA, UserRole.TenantAdmin, TenantUserRole.Admin);
        var support = await fixture.AddPlatformAdminAsync();

        var demoted = await fixture.UpdateAsync(support, admin, UserRole.Staff, TenantUserRole.Staff);
        var deactivated = await fixture.SetActiveAsync(support, admin, isActive: false);

        Assert.Equal(EmployeeAccessRules.LastManagerMessage, demoted.Error);
        Assert.Equal(EmployeeAccessRules.LastManagerMessage, deactivated.Error);
        var membership = await fixture.MembershipAsync(admin);
        Assert.Equal(TenantUserRole.Admin, membership.Role);
        Assert.Equal(TenantUserStatus.Active, membership.Status);
    }

    [Fact]
    public async Task AnotherAdmin_ShouldBeDemotedAndDeactivated_WhileTheOwnerStays()
    {
        await using var fixture = await Fixture.CreateAsync();
        var owner = await fixture.AddMemberAsync(TenantA, UserRole.TenantAdmin, TenantUserRole.Owner);
        var admin = await fixture.AddMemberAsync(TenantA, UserRole.TenantAdmin, TenantUserRole.Admin);

        var demoted = await fixture.UpdateAsync(owner, admin, UserRole.Staff, TenantUserRole.Staff);
        var deactivated = await fixture.SetActiveAsync(owner, admin, isActive: false);

        Assert.True(demoted.IsSuccess, demoted.Error);
        Assert.True(deactivated.IsSuccess, deactivated.Error);
        var membership = await fixture.MembershipAsync(admin);
        Assert.Equal(TenantUserRole.Staff, membership.Role);
        Assert.Equal(TenantUserStatus.Disabled, membership.Status);
        // Their only business: the account role follows.
        Assert.Equal(UserRole.Staff, (await fixture.UserAsync(admin)).Role);
    }

    [Fact]
    public async Task PersonWhoOwnsAnotherBusiness_ShouldKeepTheirAccountRole_WhenMadeStaffHere()
    {
        await using var fixture = await Fixture.CreateAsync();
        var owner = await fixture.AddMemberAsync(TenantA, UserRole.TenantAdmin, TenantUserRole.Owner);
        var ownerOfB = await fixture.AddMemberAsync(TenantB, UserRole.TenantAdmin, TenantUserRole.Owner);
        var helper = await fixture.AddMembershipAsync(TenantA, ownerOfB.UserId, TenantUserRole.Admin);

        var result = await fixture.UpdateAsync(owner, helper, UserRole.Staff, TenantUserRole.Staff);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(TenantUserRole.Staff, (await fixture.MembershipAsync(helper)).Role);
        // Still the owner of business B: only the membership in A changed.
        Assert.Equal(UserRole.TenantAdmin, (await fixture.UserAsync(helper)).Role);
        Assert.Equal(TenantUserRole.Owner, (await fixture.MembershipAsync(ownerOfB)).Role);
    }

    [Fact]
    public async Task SharedAccount_ShouldKeepItsName_WhenOneOfItsBusinessesEditsIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var owner = await fixture.AddMemberAsync(TenantA, UserRole.TenantAdmin, TenantUserRole.Owner);
        var ownerOfB = await fixture.AddMemberAsync(TenantB, UserRole.TenantAdmin, TenantUserRole.Owner);
        var helper = await fixture.AddMembershipAsync(TenantA, ownerOfB.UserId, TenantUserRole.Staff);

        var renamed = await fixture.UpdateAsync(owner, helper, UserRole.Staff, TenantUserRole.Staff, firstName: "Otro");

        // Business B shows the same account: only the person renames it, from their profile.
        Assert.Equal(UpdateEmployeeCommandHandler.SharedAccountNameMessage, renamed.Error);
        Assert.Equal("Persona", (await fixture.UserAsync(helper)).FirstName);
    }

    [Fact]
    public async Task WhoeverRunsTheTeam_ShouldChooseTheNoticesOfEachPerson_SavingOnlyWhatChanges()
    {
        // Fase 29Q (ADR 0071).
        await using var fixture = await Fixture.CreateAsync();
        var owner = await fixture.AddMemberAsync(TenantA, UserRole.TenantAdmin, TenantUserRole.Owner);
        var admin = await fixture.AddMemberAsync(TenantA, UserRole.TenantAdmin, TenantUserRole.Admin);
        var receptionist = await fixture.AddMemberAsync(TenantA, UserRole.Staff, TenantUserRole.Staff);
        var cashier = await fixture.AddMemberAsync(TenantA, UserRole.Staff, TenantUserRole.Staff);

        var byTheOwner = await fixture.UpdateAsync(owner, receptionist, UserRole.Staff, TenantUserRole.Staff, requestEmails: true);
        var byAnAdmin = await fixture.UpdateAsync(admin, admin, UserRole.TenantAdmin, TenantUserRole.Admin, quoteReplyEmails: true);
        // What they already get changes nothing: until someone chooses, a cashier gets none.
        var asTheyAre = await fixture.UpdateAsync(admin, cashier, UserRole.Staff, TenantUserRole.Staff, "Persona", false, false);

        Assert.All([byTheOwner, byAnAdmin, asTheyAre], result => Assert.True(result.IsSuccess, result.Error));
        Assert.Equal((true, false, false), (byTheOwner.Value!.NoticeEmails.Requests, byTheOwner.Value.NoticeEmails.QuoteReplies,
            byTheOwner.Value.NoticeEmails.EmailConfirmed));
        Assert.Equal((false, true), (byAnAdmin.Value!.NoticeEmails.Requests, byAnAdmin.Value.NoticeEmails.QuoteReplies));
        Assert.Equal(((bool?)true, (bool?)null), await fixture.ChoicesAsync(receptionist));
        Assert.Equal(((bool?)null, (bool?)true), await fixture.ChoicesAsync(admin));
        Assert.Equal(((bool?)null, (bool?)null), await fixture.ChoicesAsync(cashier));

        // Null leaves each as it is; turned off, it stays off.
        var kept = await fixture.UpdateAsync(owner, receptionist, UserRole.Staff, TenantUserRole.Staff);
        var turnedOff = await fixture.UpdateAsync(admin, receptionist, UserRole.Staff, TenantUserRole.Staff, "Persona", false);
        Assert.True(kept.Value!.NoticeEmails.Requests);
        Assert.False(turnedOff.Value!.NoticeEmails.Requests);
        Assert.Equal(((bool?)false, (bool?)null), await fixture.ChoicesAsync(receptionist));
    }

    [Fact]
    public async Task APerson_ShouldBeToldWhoChangedTheirNotices_AndWhat_ButNotForTheirOwnChange()
    {
        // Fase 30A (ADR 0081).
        await using var fixture = await Fixture.CreateAsync();
        var owner = await fixture.AddMemberAsync(TenantA, UserRole.TenantAdmin, TenantUserRole.Owner);
        var admin = await fixture.AddMemberAsync(TenantA, UserRole.TenantAdmin, TenantUserRole.Admin);
        var receptionist = await fixture.AddMemberAsync(TenantA, UserRole.Staff, TenantUserRole.Staff);

        // Both turned on by the owner: one message with both.
        var bothOn = await fixture.UpdateAsync(owner, receptionist, UserRole.Staff, TenantUserRole.Staff, "Persona", true, true);
        // Only what changes: the agenda's stays on, the quotes' go off.
        var quotesOff = await fixture.UpdateAsync(admin, receptionist, UserRole.Staff, TenantUserRole.Staff, "Persona", true, false);
        // Nothing changes, or the admin changes their own: nobody is told, though both save.
        var asTheyAre = await fixture.UpdateAsync(owner, receptionist, UserRole.Staff, TenantUserRole.Staff, "Persona", true, false);
        var theirOwn = await fixture.UpdateAsync(admin, admin, UserRole.TenantAdmin, TenantUserRole.Admin, "Persona", requestEmails: true);

        Assert.All([bothOn, quotesOff, asTheyAre, theirOwn], result => Assert.True(result.IsSuccess, result.Error));
        Assert.True((await fixture.MembershipAsync(admin)).RequestEmails);

        Assert.Equal(
            [
                (new TenantUserNoticeEmailsChangedEvent(receptionist.TenantUserId, owner.UserId, true, true) { EventId = Guid.Empty, OccurredOn = default }, (Guid?)TenantA),
                (new TenantUserNoticeEmailsChangedEvent(receptionist.TenantUserId, admin.UserId, null, false) { EventId = Guid.Empty, OccurredOn = default }, (Guid?)TenantA)
            ],
            fixture.Outbox.Added.Select(m => (m.Event with { EventId = Guid.Empty, OccurredOn = default }, m.TenantId)));
    }

    [Fact]
    public async Task WhoeverRunsTheTeam_ShouldChooseTheOrdersTheirCustomersCancel_TellingThePerson_ButNeverTheOwners()
    {
        // Fase 30B (ADR 0082).
        await using var fixture = await Fixture.CreateAsync();
        var owner = await fixture.AddMemberAsync(TenantA, UserRole.TenantAdmin, TenantUserRole.Owner);
        var admin = await fixture.AddMemberAsync(TenantA, UserRole.TenantAdmin, TenantUserRole.Admin);
        var cashier = await fixture.AddMemberAsync(TenantA, UserRole.Staff, TenantUserRole.Staff);

        var turnedOn = await fixture.UpdateAsync(admin, cashier, UserRole.Staff, TenantUserRole.Staff, orderCancellationEmails: true);
        // The owner's: refused when it changes, saved as it is when it does not (the owner gets them until they choose).
        var refused = await fixture.UpdateAsync(admin, owner, UserRole.TenantAdmin, TenantUserRole.Owner, orderCancellationEmails: false);
        var asTheyAre = await fixture.UpdateAsync(admin, owner, UserRole.TenantAdmin, TenantUserRole.Owner, orderCancellationEmails: true);

        Assert.True(turnedOn.IsSuccess, turnedOn.Error);
        Assert.Equal((true, false, false), (turnedOn.Value!.NoticeEmails.OrderCancellations, turnedOn.Value.NoticeEmails.Requests,
            turnedOn.Value.NoticeEmails.QuoteReplies));
        Assert.True((await fixture.MembershipAsync(cashier)).OrderCancellationEmails);
        Assert.Equal(EmployeeNoticeEmails.OwnerChoosesMessage, refused.Error);
        Assert.True(asTheyAre.IsSuccess, asTheyAre.Error);
        Assert.Null((await fixture.MembershipAsync(owner)).OrderCancellationEmails);
        Assert.True((await fixture.DetailAsync(owner)).NoticeEmails.OrderCancellations);
        Assert.False((await fixture.DetailAsync(admin)).NoticeEmails.OrderCancellations);
        Assert.Equal(
            [(new TenantUserNoticeEmailsChangedEvent(cashier.TenantUserId, admin.UserId, null, null, true) { EventId = Guid.Empty, OccurredOn = default }, (Guid?)TenantA)],
            fixture.Outbox.Added.Select(m => (m.Event with { EventId = Guid.Empty, OccurredOn = default }, m.TenantId)));
    }

    [Fact]
    public async Task TheOwnersNotices_ShouldOnlyChangeFromTheirProfile_NeverFromTheTeam()
    {
        await using var fixture = await Fixture.CreateAsync();
        var owner = await fixture.AddMemberAsync(TenantA, UserRole.TenantAdmin, TenantUserRole.Owner);
        var admin = await fixture.AddMemberAsync(TenantA, UserRole.TenantAdmin, TenantUserRole.Admin);

        var byAnAdmin = await fixture.UpdateAsync(admin, owner, UserRole.TenantAdmin, TenantUserRole.Owner, "Rosa", requestEmails: false);
        var byThemselves = await fixture.UpdateAsync(owner, owner, UserRole.TenantAdmin, TenantUserRole.Owner, "Rosa", quoteReplyEmails: false);
        // What they already get is no change: the rest of the form saves.
        var asTheyAre = await fixture.UpdateAsync(admin, owner, UserRole.TenantAdmin, TenantUserRole.Owner, "Rosa", true, true);

        Assert.Equal(EmployeeNoticeEmails.OwnerChoosesMessage, byAnAdmin.Error);
        Assert.Equal(EmployeeNoticeEmails.OwnerChoosesMessage, byThemselves.Error);
        Assert.True(asTheyAre.IsSuccess, asTheyAre.Error);
        Assert.Equal(((bool?)null, (bool?)null), await fixture.ChoicesAsync(owner));
        Assert.Equal("Rosa", (await fixture.UserAsync(owner)).FirstName);
    }

    [Fact]
    public async Task ARefusedChangeOfTheOwnersNotices_ShouldChangeNothingElse()
    {
        // The account's roles save as soon as they change: the refusal comes first.
        await using var fixture = await Fixture.CreateAsync();
        var owner = await fixture.AddMemberAsync(TenantA, UserRole.TenantAdmin, TenantUserRole.Owner);
        var admin = await fixture.AddMemberAsync(TenantA, UserRole.TenantAdmin, TenantUserRole.Admin);

        var refused = await fixture.UpdateAsync(admin, owner, UserRole.Staff, TenantUserRole.Staff, "Otra", requestEmails: false);

        Assert.Equal(EmployeeNoticeEmails.OwnerChoosesMessage, refused.Error);
        var account = await fixture.UserAsync(owner);
        Assert.Equal(("Persona", UserRole.TenantAdmin), (account.FirstName, account.Role));
    }

    [Fact]
    public async Task TheTeam_ShouldSayWhatEachPersonGets_InItsListAndInTheirDetail()
    {
        // The owner without a choice gets both; a receptionist who chose the agenda's, with her email confirmed, gets those.
        await using var fixture = await Fixture.CreateAsync();
        var owner = await fixture.AddMemberAsync(TenantA, UserRole.TenantAdmin, TenantUserRole.Owner);
        var receptionist = await fixture.AddMemberAsync(TenantA, UserRole.Staff, TenantUserRole.Staff);
        await fixture.ChooseAsync(receptionist, requestEmails: true);
        await fixture.ConfirmEmailAsync(receptionist);

        var list = await fixture.ListAsync(TenantA);
        var detail = await fixture.DetailAsync(receptionist);
        var ownersDetail = await fixture.DetailAsync(owner);

        static (bool, bool, bool) Notices(EmployeeDto dto) =>
            (dto.NoticeEmails.Requests, dto.NoticeEmails.QuoteReplies, dto.NoticeEmails.EmailConfirmed);
        Assert.Equal((true, false, true), Notices(list.Single(e => e.TenantUserId == receptionist.TenantUserId)));
        Assert.Equal((true, true, false), Notices(list.Single(e => e.TenantUserId == owner.TenantUserId)));
        Assert.Equal((true, false, true), Notices(detail));
        Assert.Equal((true, true, false), Notices(ownersDetail));
    }

    private sealed record Member(Guid TenantUserId, Guid UserId, Guid TenantId, string Role);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TestTenantService _tenantService = new();
        private readonly ApplicationDbContext _context;

        /// <summary>What the changes of someone else's notices told each person (Fase 30A).</summary>
        public NoticeEmailsSupport.Outbox<TenantUserNoticeEmailsChangedEvent> Outbox { get; } = new();

        private Fixture()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            _context = new ApplicationDbContext(options, _tenantService, new NullMediator());
        }

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            foreach (var role in new[] { "SUPER_ADMIN", "TENANT_ADMIN", "STAFF", "KITCHEN" })
                fixture._context.Roles.Add(new ApplicationRole(role) { NormalizedName = role });
            await fixture._context.SaveChangesAsync();
            return fixture;
        }

        public async Task<Member> AddMemberAsync(Guid tenantId, UserRole appRole, TenantUserRole tenantRole)
        {
            var user = NewUser(appRole);
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            return await AddMembershipAsync(tenantId, user.Id, tenantRole);
        }

        public async Task<Member> AddMembershipAsync(Guid tenantId, Guid userId, TenantUserRole tenantRole)
        {
            var membership = new TenantUser(tenantId, userId, tenantRole);
            _context.TenantUsers.Add(membership);
            await _context.SaveChangesAsync();
            var role = tenantRole is TenantUserRole.Owner or TenantUserRole.Admin ? "TENANT_ADMIN" : "STAFF";
            return new Member(membership.Id, userId, tenantId, role);
        }

        public async Task<Member> AddPlatformAdminAsync()
        {
            var user = NewUser(UserRole.SuperAdmin);
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            return new Member(Guid.Empty, user.Id, Guid.Empty, "SUPER_ADMIN");
        }

        public async Task DisableAsync(Member member)
        {
            var membership = await _context.TenantUsers.IgnoreQueryFilters().SingleAsync(x => x.Id == member.TenantUserId);
            membership.SetStatus(TenantUserStatus.Disabled);
            await _context.SaveChangesAsync();
        }

        public Task<Result<EmployeeDto>> UpdateAsync(
            Member actor, Member employee, UserRole appRole, TenantUserRole tenantRole, string firstName = "Persona",
            bool? requestEmails = null, bool? quoteReplyEmails = null, bool? orderCancellationEmails = null)
        {
            _tenantService.SetTenant(employee.TenantId);
            var handler = new UpdateEmployeeCommandHandler(
                _context, _tenantService, new TestCurrentUserService(actor), new TestUserManager(_context), Outbox);
            return handler.Handle(new UpdateEmployeeCommand
            {
                TenantUserId = employee.TenantUserId,
                FirstName = firstName,
                LastName = "Demo",
                AppRole = appRole,
                TenantRole = tenantRole,
                RequestEmails = requestEmails,
                QuoteReplyEmails = quoteReplyEmails,
                OrderCancellationEmails = orderCancellationEmails
            }, CancellationToken.None);
        }

        public Task<Result> SetActiveAsync(Member actor, Member employee, bool isActive)
        {
            _tenantService.SetTenant(employee.TenantId);
            var handler = new SetEmployeeActiveCommandHandler(_context, _tenantService, new TestCurrentUserService(actor));
            return handler.Handle(
                new SetEmployeeActiveCommand { TenantUserId = employee.TenantUserId, IsActive = isActive },
                CancellationToken.None);
        }

        public Task<TenantUser> MembershipAsync(Member member) =>
            _context.TenantUsers.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == member.TenantUserId);

        public async Task ChooseAsync(Member member, bool? requestEmails = null, bool? quoteReplyEmails = null)
        {
            var membership = await _context.TenantUsers.IgnoreQueryFilters().SingleAsync(x => x.Id == member.TenantUserId);
            if (requestEmails is { } requests)
                membership.ChooseRequestEmails(requests);
            if (quoteReplyEmails is { } quoteReplies)
                membership.ChooseQuoteReplyEmails(quoteReplies);
            await _context.SaveChangesAsync();
        }

        public async Task ConfirmEmailAsync(Member member)
        {
            var user = await _context.Users.IgnoreQueryFilters().SingleAsync(x => x.Id == member.UserId);
            user.EmailConfirmed = true;
            await _context.SaveChangesAsync();
        }

        public async Task<IReadOnlyList<EmployeeDto>> ListAsync(Guid tenantId)
        {
            _tenantService.SetTenant(tenantId);
            return (await new GetEmployeesQueryHandler(_context, _tenantService)
                .Handle(new GetEmployeesQuery { PageSize = 50 }, CancellationToken.None)).Items;
        }

        public async Task<EmployeeDto> DetailAsync(Member member)
        {
            _tenantService.SetTenant(member.TenantId);
            var result = await new GetEmployeeByIdQueryHandler(_context, _tenantService)
                .Handle(new GetEmployeeByIdQuery(member.TenantUserId), CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error);
            return result.Value!;
        }

        public async Task<(bool? Requests, bool? QuoteReplies)> ChoicesAsync(Member member)
        {
            var membership = await MembershipAsync(member);
            return (membership.RequestEmails, membership.QuoteReplyEmails);
        }

        public Task<ApplicationUser> UserAsync(Member member) =>
            _context.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == member.UserId);

        public ValueTask DisposeAsync() => _context.DisposeAsync();

        private static ApplicationUser NewUser(UserRole role)
        {
            var email = $"{Guid.NewGuid():N}@negocio.test";
            return new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = email,
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                // Accounts created through Identity always carry one; updating the roles requires it.
                SecurityStamp = Guid.NewGuid().ToString(),
                FirstName = "Persona",
                LastName = "Demo",
                Role = role,
                IsActive = true
            };
        }
    }

    private sealed class TestUserManager(ApplicationDbContext context) : UserManager<ApplicationUser>(
        new UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid>(context),
        Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
        new PasswordHasher<ApplicationUser>(),
        [],
        [],
        new UpperInvariantLookupNormalizer(),
        new IdentityErrorDescriber(),
        new ServiceCollection().BuildServiceProvider(),
        new NullLogger<UserManager<ApplicationUser>>());

    private sealed class TestCurrentUserService(Member actor) : ICurrentUserService
    {
        public string? UserId { get; } = actor.UserId.ToString();
        public string? UserName => "persona@negocio.test";
        public string? Role { get; } = actor.Role;
        public string? TenantRole => null;
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }

    private sealed class TestTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class NullMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default) => Task.FromResult<TResponse>(default!);
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => Task.CompletedTask;
        public Task Publish(object notification, CancellationToken ct = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => AsyncEnumerable.Empty<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => AsyncEnumerable.Empty<object?>();
        public Task<object?> Send(object request, CancellationToken ct = default) => Task.FromResult<object?>(null);
    }
}
