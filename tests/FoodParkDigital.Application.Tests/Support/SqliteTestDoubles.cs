using System.Data.Common;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FoodParkDigital.Application.Tests.Support;

/// <summary>
/// Runs a one-shot callback right before the next SaveChanges whose context matches, after the
/// code under test ran its checks and before it writes, e.g. to act as a concurrent request.
/// </summary>
internal sealed class BeforeSaveInterceptor : SaveChangesInterceptor
{
    private Func<DbContext, bool>? _when;
    private Func<DbContext, Task>? _action;

    public bool Fired { get; private set; }

    public void Arm(Func<DbContext, bool> when, Func<DbContext, Task> action)
    {
        _when = when;
        _action = action;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context && _action is { } action && _when!(context))
        {
            // Disarm first so saves made by the callback itself do not trigger it again.
            _action = null;
            _when = null;
            Fired = true;
            await action(context);
        }

        return result;
    }
}

/// <summary>Throws once when the next command matching the predicate is about to execute.</summary>
internal sealed class CommandFaultInterceptor : DbCommandInterceptor
{
    private Func<DbCommand, bool>? _matches;
    private Func<Exception>? _fault;

    public bool Fired { get; private set; }

    public void FailNext(Func<DbCommand, bool> matches, Func<Exception> fault)
    {
        _matches = matches;
        _fault = fault;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        ThrowIfArmed(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        ThrowIfArmed(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        ThrowIfArmed(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ThrowIfArmed(command);
        return ValueTask.FromResult(result);
    }

    private void ThrowIfArmed(DbCommand command)
    {
        if (_fault is not { } fault || !_matches!(command))
            return;

        _fault = null;
        _matches = null;
        Fired = true;
        throw fault();
    }
}

/// <summary>Makes the next transaction commits fail with a retriable error before reaching SQLite.</summary>
internal sealed class CommitFaultInterceptor : DbTransactionInterceptor
{
    public int Remaining { get; private set; }

    public void FailNext(int commits = 1) => Remaining = commits;

    public override InterceptionResult TransactionCommitting(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
    {
        ThrowIfArmed();
        return result;
    }

    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        ThrowIfArmed();
        return ValueTask.FromResult(result);
    }

    private void ThrowIfArmed()
    {
        if (Remaining == 0)
            return;

        Remaining--;
        throw new SimulatedTransientException();
    }
}

internal sealed class RecordingEmailService : IEmailService
{
    public List<string> ConfirmationRecipients { get; } = [];
    /// <summary>What the confirmation link carries, to open it as the person would.</summary>
    public List<(string Email, string UserId, string Token)> Confirmations { get; } = [];

    public Task SendEmailConfirmationAsync(string email, string userId, string token)
    {
        ConfirmationRecipients.Add(email);
        Confirmations.Add((email, userId, token));
        return Task.CompletedTask;
    }

    public Task SendPasswordResetEmailAsync(string email, string userId, string token) => Task.CompletedTask;

    public Task SendEmailAsync(string to, string subject, string body) => Task.CompletedTask;

    public List<(string Email, string Business, string Path)> Invitations { get; } = [];

    public Task SendTeamInvitationAsync(string email, string businessName, string invitationPath)
    {
        Invitations.Add((email, businessName, invitationPath));
        return Task.CompletedTask;
    }
}

internal sealed class RecordingSmsService : ISmsService
{
    public List<string> VerificationRecipients { get; } = [];
    public List<(string Phone, string Code)> Codes { get; } = [];

    public Task SendVerificationCodeAsync(string phoneNumber, string code)
    {
        VerificationRecipients.Add(phoneNumber);
        Codes.Add((phoneNumber, code));
        return Task.CompletedTask;
    }

    public Task SendSmsAsync(string phoneNumber, string message) => Task.CompletedTask;
}

/// <summary>
/// Records the events the requests add to the outbox (<see cref="Added"/>); <see cref="SqliteTestHost.DispatchAsync{TEvent, THandler}"/>
/// runs a handler for the ones not dispatched yet, as the outbox dispatcher does after the commit.
/// </summary>
internal sealed class RecordingOutbox : IOutbox
{
    private readonly List<(DomainEvent Event, Guid? TenantId)> _pending = [];

    public List<(DomainEvent Event, Guid? TenantId)> Added { get; } = [];

    public void Add(DomainEvent domainEvent, Guid? tenantId)
    {
        Added.Add((domainEvent, tenantId));
        _pending.Add((domainEvent, tenantId));
    }

    public List<(TEvent Event, Guid? TenantId)> TakePending<TEvent>()
        where TEvent : DomainEvent
    {
        var taken = _pending.Where(p => p.Event is TEvent).Select(p => ((TEvent)p.Event, p.TenantId)).ToList();
        _pending.RemoveAll(p => p.Event is TEvent);
        return taken;
    }
}
