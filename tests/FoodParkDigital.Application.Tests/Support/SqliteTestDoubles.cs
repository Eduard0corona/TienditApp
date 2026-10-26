using System.Data.Common;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Infrastructure.BackgroundJobs.Outbox;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>What the reset link carries.</summary>
    public List<(string Email, string UserId, string Token)> PasswordResets { get; } = [];

    public Task SendPasswordResetEmailAsync(string email, string userId, string token)
    {
        PasswordResets.Add((email, userId, token));
        return Task.CompletedTask;
    }

    public Task SendEmailAsync(string to, string subject, string body) => Task.CompletedTask;

    /// <summary>The emails with documents (the CFDI of an invoice, Fase 28E) the server took.</summary>
    public List<DocumentEmail> Documents { get; } = [];
    /// <summary>How the next emails with documents end; set a failure to make them throw, as a server that does not answer.</summary>
    public EmailSendOutcome DocumentsOutcome { get; set; } = EmailSendOutcome.Sent;
    public Exception? DocumentsFailure { get; set; }

    public Task<EmailSendOutcome> SendDocumentsAsync(DocumentEmail email, CancellationToken cancellationToken)
    {
        if (DocumentsFailure is not null)
            throw DocumentsFailure;
        if (DocumentsOutcome == EmailSendOutcome.Sent)
            Documents.Add(email);
        return Task.FromResult(DocumentsOutcome);
    }

    public List<(string Email, string Business, string Path)> Invitations { get; } = [];

    public Task SendTeamInvitationAsync(string email, string businessName, string invitationPath)
    {
        Invitations.Add((email, businessName, invitationPath));
        return Task.CompletedTask;
    }

    /// <summary>Who signed up again with the email of their confirmed account.</summary>
    public List<string> AlreadyExistsRecipients { get; } = [];

    /// <summary>Whether each "already exists" email was told the account is a customer's.</summary>
    public List<bool> AlreadyExistsToCustomers { get; } = [];

    public Task SendAccountAlreadyExistsAsync(string email, bool customerAccount)
    {
        AlreadyExistsRecipients.Add(email);
        AlreadyExistsToCustomers.Add(customerAccount);
        return Task.CompletedTask;
    }

    /// <summary>The link of each "you already started an account" email.</summary>
    public List<(string Email, string UserId, string Token)> Unfinished { get; } = [];

    public Task SendUnfinishedAccountAsync(string email, string userId, string token)
    {
        Unfinished.Add((email, userId, token));
        return Task.CompletedTask;
    }

    /// <summary>Every email sent, of any kind.</summary>
    public int Sent => Confirmations.Count + PasswordResets.Count + Invitations.Count + AlreadyExistsRecipients.Count + Unfinished.Count;
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
/// Records the events the requests add to the outbox (<see cref="Added"/>) and stores them through the real
/// <see cref="OutboxWriter"/>, in the context of the request: an event is committed, or rolled back, with its request,
/// as in production (a retried transaction keeps only the attempt that committed).
/// <see cref="SqliteTestHost.DispatchAsync{TEvent, THandler}"/> runs a handler for the committed ones not dispatched
/// yet, as the outbox dispatcher does after the commit.
/// </summary>
internal sealed class RecordingOutbox
{
    private readonly HashSet<Guid> _dispatched = [];

    /// <summary>Every event added, committed or not.</summary>
    public List<(DomainEvent Event, Guid? TenantId)> Added { get; } = [];

    /// <summary>The outbox of one request (DI scope).</summary>
    public IOutbox For(IServiceProvider services)
        => new ScopedOutbox(this, ActivatorUtilities.CreateInstance<OutboxWriter>(services));

    /// <summary>The committed events of <typeparamref name="TEvent"/> not dispatched yet, in the order they were stored.</summary>
    public async Task<List<(TEvent Event, Guid? TenantId)>> TakeCommittedAsync<TEvent>(ApplicationDbContext context)
        where TEvent : DomainEvent
    {
        var typeName = typeof(TEvent).FullName!;
        var stored = await context.OutboxMessages.AsNoTracking().Where(m => m.Type == typeName).ToListAsync();
        var order = Added.Select((added, index) => (added.Event.EventId, index)).ToDictionary(x => x.EventId, x => x.index);
        return stored
            .Where(message => _dispatched.Add(message.Id))
            .OrderBy(message => order.GetValueOrDefault(message.Id, int.MaxValue))
            .Select(message => ((TEvent)OutboxEventSerializer.Deserialize(message.Type, message.Payload)!, message.TenantId))
            .ToList();
    }

    private sealed class ScopedOutbox(RecordingOutbox recorder, OutboxWriter writer) : IOutbox
    {
        public void Add(DomainEvent domainEvent, Guid? tenantId)
        {
            recorder.Added.Add((domainEvent, tenantId));
            writer.Add(domainEvent, tenantId);
        }
    }
}
