using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Employees;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Identity.Events;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Identity.EventHandlers;

/// <summary>
/// Sends the email a request asked for, after its answer: finds the account, checks it may still receive it, creates
/// its link (never stored in the outbox) and sends it. An account that does not exist or may no longer receive it
/// gets nothing, silently: the request already answered the same for everyone. A provider failure throws, and the
/// outbox retries with a new link.
/// </summary>
public sealed class AccountEmailRequestedHandler : IOutboxEventHandler<AccountEmailRequestedEvent>
{
    /// <summary>Past this, the person has asked again or given up: a late email would only confuse them.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromDays(1);

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApplicationDbContext _context;
    private readonly IEmailService _email;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<AccountEmailRequestedHandler> _logger;

    public AccountEmailRequestedHandler(
        UserManager<ApplicationUser> userManager,
        IApplicationDbContext context,
        IEmailService email,
        IDateTimeProvider clock,
        ILogger<AccountEmailRequestedHandler> logger)
    {
        _userManager = userManager;
        _context = context;
        _email = email;
        _clock = clock;
        _logger = logger;
    }

    public async Task HandleAsync(
        AccountEmailRequestedEvent domainEvent,
        OutboxEventContext context,
        CancellationToken cancellationToken)
    {
        if (_clock.UtcNow - context.OccurredAt > MaxDelay)
        {
            _logger.LogWarning(
                "An account email ({Purpose}) asked for at {RequestedAt} was not sent: too late.",
                domainEvent.Purpose,
                context.OccurredAt);
            return;
        }

        var user = await FindAccountAsync(domainEvent, cancellationToken);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
            return;

        switch (domainEvent.Purpose)
        {
            case AccountEmailPurpose.EmailConfirmation:
                if (!user.EmailConfirmed)
                    await SendActivationAsync(user, cancellationToken);
                break;

            case AccountEmailPurpose.PasswordReset:
                // As before the outbox: only an account that confirmed its email resets its password by email.
                if (user.EmailConfirmed)
                {
                    var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                    await _email.SendPasswordResetEmailAsync(user.Email, user.Id.ToString(), token);
                }
                break;

            case AccountEmailPurpose.SignUpWithExistingEmail:
                // A deactivated account gets nothing: it cannot sign in, and nobody is waiting for the email.
                if (!user.IsActive)
                    break;
                if (user.EmailConfirmed)
                    await _email.SendAccountAlreadyExistsAsync(user.Email, user.Role == UserRole.Customer);
                else if (!await _userManager.HasPasswordAsync(user))
                    // Invited to a team and never accepted: the invitation is how that account enters.
                    await SendPendingInvitationsAsync(user, cancellationToken);
                else
                {
                    // Started and never activated: this sign-up was not saved, and the email says so. Its link
                    // activates the account started then, which whoever did not start it is told not to open.
                    var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                    await _email.SendUnfinishedAccountAsync(user.Email, user.Id.ToString(), token);
                }
                break;

            case AccountEmailPurpose.TeamInvitation:
                await SendInvitationAsync(user, context.TenantId, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// The link that activates the account; an account invited to a team, without a password yet, gets its pending
    /// invitations instead (activating its email would leave it unable to sign in).
    /// </summary>
    private async Task SendActivationAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        if (!await _userManager.HasPasswordAsync(user))
        {
            await SendPendingInvitationsAsync(user, cancellationToken);
            return;
        }

        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        await _email.SendEmailConfirmationAsync(user.Email!, user.Id.ToString(), token);
    }

    /// <summary>One invitation link, which accepts every pending invitation of the account; none pending, nothing.</summary>
    private async Task SendPendingInvitationsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var businesses = await EmployeeInvitations.PendingBusinessesAsync(_context, user.Id, cancellationToken);
        if (businesses.Count == 0)
            return;

        var path = await EmployeeInvitations.CreatePathAsync(_userManager, user);
        await _email.SendTeamInvitationAsync(user.Email!, string.Join(", ", businesses), path);
    }

    private async Task SendInvitationAsync(ApplicationUser user, Guid? tenantId, CancellationToken cancellationToken)
    {
        if (tenantId is not { } businessId)
            return;

        // The scope runs in the business of the event; the filter names it as well. An invitation accepted, or
        // removed, before the email went out is no longer sent.
        var pending = await _context.TenantUsers.AnyAsync(
            x => x.TenantId == businessId && x.UserId == user.Id && x.Status == TenantUserStatus.Invited,
            cancellationToken);
        if (!pending)
            return;

        var businessName = await _context.Tenants
            .Where(t => t.Id == businessId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "tu negocio";
        // A link of its own: the one the admin got to share by WhatsApp stays valid as well.
        var path = await EmployeeInvitations.CreatePathAsync(_userManager, user);
        await _email.SendTeamInvitationAsync(user.Email!, businessName, path);
    }

    private Task<ApplicationUser?> FindAccountAsync(AccountEmailRequestedEvent request, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: accounts belong to no business, and most of these requests came without a session.
        var users = _context.Users.IgnoreQueryFilters();
        if (request.UserId is { } userId)
            return users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        var normalizedEmail = request.Email?.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(normalizedEmail)
            ? Task.FromResult<ApplicationUser?>(null)
            : users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
    }
}
