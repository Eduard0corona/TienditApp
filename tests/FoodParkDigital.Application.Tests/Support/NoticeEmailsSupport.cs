using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.Application.Tests.ExploreResponseTimeTests;

namespace FoodParkDigital.Application.Tests.Support;

/// <summary>
/// What the tests of the notices by email share (Fases 29N and 29O, ADR 0068 and 0069): members with accounts of their own and
/// their choices, their access as the API reads it, and the outbox, provider, links and screens the handlers write to.
/// </summary>
internal static class NoticeEmailsSupport
{
    public const string AppUrl = "https://app.tienditapp.test";

    public static OutboxEventContext Context(Tenant tenant) => new(Guid.NewGuid(), tenant.Id, null, null, Now, 1);

    /// <summary>A member of <paramref name="tenant"/> with an account of their own, and its id.</summary>
    /// <param name="choose">What they chose for the agenda's notices (Fase 29N), if they chose.</param>
    /// <param name="chooseQuotes">What they chose for the answers to quotes (Fase 29O), if they chose.</param>
    /// <param name="chooseOrderCancellations">What they chose for the online orders their customers cancel (Fase 30B), if they chose.</param>
    public static async Task<Guid> MemberAsync(
        SalesTestFixture fixture,
        Tenant tenant,
        string name,
        TenantUserRole role,
        bool? choose = null,
        TenantPermission[]? permissions = null,
        TenantUserStatus status = TenantUserStatus.Active,
        bool emailConfirmed = true,
        bool accountActive = true,
        bool? chooseQuotes = null,
        bool? chooseOrderCancellations = null)
    {
        var email = $"{name}.{tenant.Slug}@demo.com";
        var account = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = emailConfirmed,
            FirstName = name,
            LastName = "Equipo",
            Role = role == TenantUserRole.Staff ? UserRole.Staff : UserRole.TenantAdmin,
            IsActive = accountActive
        };
        var member = Membership(tenant, account.Id, role, status);
        if (permissions is not null)
            member.SetExplicitPermissions(permissions.Select(p => p.ToString()));
        if (choose is { } chosen)
            member.ChooseRequestEmails(chosen);
        if (chooseQuotes is { } chosenForQuotes)
            member.ChooseQuoteReplyEmails(chosenForQuotes);
        if (chooseOrderCancellations is { } chosenForOrders)
            member.ChooseOrderCancellationEmails(chosenForOrders);
        fixture.Context.Users.Add(account);
        fixture.Context.TenantUsers.Add(member);
        await fixture.Context.SaveChangesAsync();
        return account.Id;
    }

    public static TenantUser Membership(Tenant tenant, Guid userId, TenantUserRole role, TenantUserStatus status = TenantUserStatus.Active)
        => new(tenant.Id, userId, role, status);

    /// <summary>The access of each member as the API reads it (TenantAccessService), from what the fixture saved.</summary>
    public sealed class MembersAccess(SalesDbContext context) : ITenantAccessService
    {
        public async Task<TenantAccessSnapshot> GetAccessSnapshotAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
        {
            var member = await context.TenantUsers.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId, cancellationToken);
            var tenant = await context.Tenants.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == tenantId, cancellationToken);
            var settings = await context.TenantSettings.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.TenantId == tenantId, cancellationToken);
            var capabilities = TenantCapabilityRules.NormalizeCapabilities(settings.Capabilities, tenant.BusinessType);
            if (member is not { Status: TenantUserStatus.Active })
                return new TenantAccessSnapshot(false, capabilities, []);
            var account = await context.Users.AsNoTracking().SingleAsync(u => u.Id == userId, cancellationToken);
            return new TenantAccessSnapshot(
                true,
                capabilities,
                TenantPermissionCatalog.ResolveEffectiveCodes(member.PermissionCodes, account.Role, member.Role),
                TenantSuspended: tenant.Status is TenantStatus.Suspended or TenantStatus.Inactive,
                IsManager: member.Role is TenantUserRole.Owner or TenantUserRole.Admin);
        }

        public Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> HasCapabilityAsync(Guid tenantId, TenantCapability capability, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> HasAnyCapabilityAsync(Guid tenantId, TenantCapability capabilities, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, TenantPermission permission, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>The messages a handler added to the outbox, each one of <typeparamref name="TEvent"/>.</summary>
    public sealed class Outbox<TEvent> : IOutbox where TEvent : DomainEvent
    {
        public List<(TEvent Event, Guid? TenantId)> Added { get; } = [];

        public void Add(DomainEvent domainEvent, Guid? tenantId) => Added.Add(((TEvent)domainEvent, tenantId));
    }

    public sealed class Emails(bool fail = false) : IEmailService
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];

        public Task SendEmailAsync(string to, string subject, string body)
        {
            if (fail)
                throw new InvalidOperationException("SMTP 421");
            Sent.Add((to, subject, body));
            return Task.CompletedTask;
        }

        public Task SendEmailConfirmationAsync(string email, string userId, string token) => throw new NotSupportedException();

        public Task SendPasswordResetEmailAsync(string email, string userId, string token) => throw new NotSupportedException();

        public Task SendTeamInvitationAsync(string email, string businessName, string invitationPath) => throw new NotSupportedException();

        public Task SendAccountAlreadyExistsAsync(string email, bool customerAccount) => throw new NotSupportedException();

        public Task SendUnfinishedAccountAsync(string email, string userId, string token) => throw new NotSupportedException();

        public Task<EmailSendOutcome> SendDocumentsAsync(DocumentEmail email, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    public sealed class Links(string? baseUrl) : IAppLinks
    {
        public string? BaseUrl => baseUrl;
    }

    /// <summary>What the live screens of the agenda heard.</summary>
    public sealed class Screens(bool fail = false) : IAppointmentNotificationService
    {
        public List<(Guid TenantId, AppointmentRequestedNotice Notice)> Heard { get; } = [];

        public List<(Guid TenantId, AppointmentCancelledNotice Notice)> Cancelled { get; } = [];

        public Task NotifyAppointmentRequested(Guid tenantId, AppointmentRequestedNotice notice)
        {
            if (fail)
                throw new InvalidOperationException("SignalR");
            Heard.Add((tenantId, notice));
            return Task.CompletedTask;
        }

        public Task NotifyAppointmentCancelled(Guid tenantId, AppointmentCancelledNotice notice)
        {
            if (fail)
                throw new InvalidOperationException("SignalR");
            Cancelled.Add((tenantId, notice));
            return Task.CompletedTask;
        }
    }
}
