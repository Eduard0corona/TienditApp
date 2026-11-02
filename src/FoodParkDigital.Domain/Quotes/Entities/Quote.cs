using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Taxes;

namespace FoodParkDigital.Domain.Quotes.Entities;

/// <summary>
/// A version of what the business proposes after looking at the job (Fase 29C, ADR 0053 points 14, 15 and 17). The versions
/// of a quote share its folio (<see cref="Number"/>) and count from 1 (<see cref="Revision"/>). A draft is edited; once sent
/// it says the same forever: correcting it starts the next version, and when that one is sent this one is
/// <see cref="QuoteStatus.Superseded"/>. A sent quote reads expired once its validity passed, and the business records whether
/// the customer accepted or rejected it. The server works out its taxes and total: the browser never sends one. An accepted
/// version becomes the job once (Fase 29E): a service order with its lines (<see cref="OrderId"/>).
/// </summary>
public class Quote : AuditableEntity, ITenantScoped, IAggregateRoot
{
    private readonly List<QuoteLine> _lines = new();

    private Quote() { }

    private Quote(Guid tenantId, string number, int revision, QuoteCustomer customer, Guid? appointmentId, bool pricesIncludeTax,
        int validityDays, string? note, QuoteVisitCredit? visitCredit, DateTimeOffset createdAt)
    {
        TenantId = tenantId;
        Number = number;
        Revision = revision;
        Status = QuoteStatus.Draft;
        AppointmentId = appointmentId;
        PricesIncludeTax = pricesIncludeTax;
        CreatedAt = createdAt;
        SetCustomer(customer);
        SetValidityDays(validityDays);
        SetNote(note);
        SetVisitCreditCore(visitCredit);
    }

    public Guid TenantId { get; private set; }
    /// <summary>The folio of the quote (COT-000123), the same in all its versions.</summary>
    public string Number { get; private set; } = null!;
    /// <summary>Which version of the quote this is, from 1.</summary>
    public int Revision { get; private set; }
    /// <summary>What is stored; <see cref="StatusOn"/> says whether a sent one already expired.</summary>
    public QuoteStatus Status { get; private set; }

    public Guid? CustomerId { get; private set; }
    public string CustomerName { get; private set; } = null!;
    public string CustomerPhone { get; private set; } = null!;
    public string? CustomerEmail { get; private set; }
    /// <summary>The appointment of the visit it comes from, when it comes from one.</summary>
    public Guid? AppointmentId { get; private set; }

    /// <summary>Whether its prices include IVA: the business's policy when the quote started, kept by all its versions.</summary>
    public bool PricesIncludeTax { get; private set; }
    /// <summary>How many days it is valid once sent.</summary>
    public int ValidityDays { get; private set; }
    /// <summary>The last day (of the business) it is valid; set when it is sent.</summary>
    public DateOnly? ValidUntil { get; private set; }
    public string? Note { get; private set; }

    public decimal SubTotal { get; private set; }
    public decimal Tax { get; private set; }
    public decimal Total { get; private set; }

    /// <summary>The sale of the visit it takes off (with <see cref="VisitCreditAmount"/>); null when it takes nothing off.</summary>
    public Guid? VisitCreditSaleOrderId { get; private set; }
    public decimal? VisitCreditAmount { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }
    /// <summary>The link of this version, given when it is sent (<see cref="Order.TrackingTokenLength"/> characters).</summary>
    public string? TrackingToken { get; private set; }
    /// <summary>When the business recorded the customer's answer, or replaced, or discarded it.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }
    public QuoteReplyChannel? ReplyChannel { get; private set; }
    /// <summary>What the customer answered, in the business's words.</summary>
    public string? CustomerResponse { get; private set; }

    /// <summary>The service order an accepted version became (Fase 29E); null until it becomes one.</summary>
    public Guid? OrderId { get; private set; }
    public DateTimeOffset? ConvertedAt { get; private set; }
    /// <summary>
    /// What was paid for the visit and became a payment of that order (<see cref="OrderId"/>); null when nothing was. From
    /// then on it is what the quote takes off for the visit, whatever happens to the sale of the visit.
    /// </summary>
    public decimal? AppliedVisitCredit { get; private set; }

    public byte[] Version { get; private set; } = null!;

    public IReadOnlyCollection<QuoteLine> Lines => _lines.AsReadOnly();

    public QuoteVisitCredit? VisitCredit => VisitCreditSaleOrderId is { } sale && VisitCreditAmount is { } amount
        ? new QuoteVisitCredit(sale, amount)
        : null;

    /// <summary>A new quote: its first version, a draft without lines.</summary>
    /// <param name="number">Its folio, from the business's counter.</param>
    /// <param name="validityDays">The business's validity (<see cref="Tenants.Entities.TenantSettings.QuoteValidityDays"/>).</param>
    public static Quote Start(Guid tenantId, string number, QuoteCustomer customer, Guid? appointmentId, bool pricesIncludeTax,
        int validityDays, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("The business is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(number))
            throw new ArgumentException("The folio is required.", nameof(number));
        ArgumentNullException.ThrowIfNull(customer);
        if (appointmentId == Guid.Empty)
            throw new ArgumentException("The appointment is required when given.", nameof(appointmentId));

        return new Quote(tenantId, number.Trim(), 1, customer, appointmentId, pricesIncludeTax, validityDays, null, null, now);
    }

    /// <summary>The status on the business's <paramref name="today"/>: a sent quote whose validity passed reads expired.</summary>
    public QuoteStatus StatusOn(DateOnly today)
        => Status == QuoteStatus.Sent && ValidUntil is { } until && today > until ? QuoteStatus.Expired : Status;

    /// <summary>Whether its link still opens: sent at some point and up to 30 days after its validity.</summary>
    public bool LinkOpensOn(DateOnly today)
        => TrackingToken is not null && ValidUntil is { } until && today <= until.AddDays(QuoteRules.LinkDaysAfterValidity);

    /// <summary>
    /// What it takes off for the visit while its sale still has <paramref name="stillPaidForTheVisit"/> paid for it (after
    /// what was refunded or returned; null once that sale was cancelled): what was fixed, never more than that. Once it became
    /// a service order, what was applied to that order (<see cref="AppliedVisitCredit"/>): it left the sale of the visit then.
    /// </summary>
    public decimal VisitCreditStanding(decimal? stillPaidForTheVisit)
        => OrderId is not null ? AppliedVisitCredit ?? 0m
            : VisitCreditAmount is { } credit && stillPaidForTheVisit is { } paid ? Math.Min(credit, Math.Max(0m, paid)) : 0m;

    /// <summary>What is left to pay: the total less what it takes off for the visit (<see cref="VisitCreditStanding"/>).</summary>
    public decimal ToPay(decimal? stillPaidForTheVisit)
        => Math.Max(0m, Total - VisitCreditStanding(stillPaidForTheVisit));

    public IReadOnlyList<OrderTaxLine> GetTaxBreakdown()
        => OrderTaxCalculator.Calculate(PricesIncludeTax, _lines.Select(line => new TaxableAmount(line.Total, line.VatRate)));

    /// <summary>Who it is for, its validity and its note, while it is a draft.</summary>
    public void UpdateDetails(QuoteCustomer customer, int validityDays, string? note, DateTimeOffset now)
    {
        EnsureDraft();
        SetCustomer(customer);
        SetValidityDays(validityDays);
        SetNote(note);
        UpdatedAt = now;
    }

    /// <summary>Its lines, in this order, while it is a draft; its totals follow them.</summary>
    public void ReplaceLines(IReadOnlyCollection<QuoteLineInput> lines, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lines);
        EnsureDraft();
        if (lines.Count > QuoteRules.MaxLines)
            throw new DomainException(QuoteRules.TooManyLinesMessage);

        _lines.Clear();
        var position = 1;
        foreach (var line in lines)
            _lines.Add(new QuoteLine(TenantId, Id, position++, line));
        RecalculateTotals();
        UpdatedAt = now;
    }

    /// <summary>
    /// What the customer paid for the visit, as the server works it out from the sale of the visit while it is a draft; null
    /// when nothing is taken off. Only a quote that comes from a visit takes it off.
    /// </summary>
    public void SetVisitCredit(QuoteVisitCredit? credit, DateTimeOffset now)
    {
        EnsureDraft();
        SetVisitCreditCore(credit);
        UpdatedAt = now;
    }

    /// <summary>
    /// The customer gets it: it is valid from the business's <paramref name="today"/> for its days, with its link. A corrected
    /// version replaces the one the customer had (<paramref name="replaced"/>): the latest version sent or rejected before it.
    /// </summary>
    public void Send(DateOnly today, string trackingToken, Quote? replaced, DateTimeOffset now)
    {
        EnsureDraft();
        if (_lines.Count == 0 || Total <= 0)
            throw new DomainException(QuoteRules.NothingToSendMessage);
        if (!Order.IsTrackingToken(trackingToken))
            throw new ArgumentException("The link must look like one.", nameof(trackingToken));
        if (Revision == 1 ? replaced is not null : replaced is null)
            throw new ArgumentException("A corrected version replaces the previous one, and only it does.", nameof(replaced));

        if (replaced is not null)
        {
            if (replaced.TenantId != TenantId || replaced.Number != Number || replaced.Revision >= Revision)
                throw new ArgumentException("The replaced version is an earlier version of this quote.", nameof(replaced));
            replaced.Supersede(now);
        }

        Status = QuoteStatus.Sent;
        ValidUntil = today.AddDays(ValidityDays);
        TrackingToken = trackingToken;
        SentAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// The next version, a draft with what this one says, to correct it. Only the version the customer has: sent (even
    /// expired) or rejected. This one stays as it is until the new one is sent.
    /// </summary>
    /// <param name="revision">Its number: after every version of the quote, the discarded drafts included.</param>
    public Quote Revise(int revision, DateTimeOffset now)
    {
        if (Status is not (QuoteStatus.Sent or QuoteStatus.Rejected))
            throw new DomainException(QuoteRules.CannotReviseMessage);
        if (revision <= Revision)
            throw new ArgumentOutOfRangeException(nameof(revision), "A correction comes after the version it corrects.");
        // This version is touched: a change of it at the same time (another correction sent, the customer's answer) collides
        // on its version instead of the correction copying what it said before.
        UpdatedAt = now;

        var draft = new Quote(TenantId, Number, revision,
            new QuoteCustomer(CustomerId, CustomerName, CustomerPhone, CustomerEmail),
            AppointmentId, PricesIncludeTax, ValidityDays, Note, VisitCredit, now);
        foreach (var line in _lines.OrderBy(line => line.Position))
            draft._lines.Add(new QuoteLine(TenantId, draft.Id, line.Position,
                new QuoteLineInput(line.ProductId, line.Description, line.Quantity, line.UnitPrice, line.VatRate)));
        draft.RecalculateTotals();
        return draft;
    }

    /// <summary>The business drops a draft before sending it. It stays, so its folio and version are not given again.</summary>
    public void Discard(DateTimeOffset now)
    {
        if (Status != QuoteStatus.Draft)
            throw new DomainException(QuoteRules.DraftOnlyDiscardMessage);
        Status = QuoteStatus.Discarded;
        ClosedAt = now;
        UpdatedAt = now;
    }

    /// <summary>The customer accepted it, while it is valid on the business's <paramref name="today"/>.</summary>
    public void Accept(DateOnly today, QuoteReplyChannel channel, string? response, DateTimeOffset now)
        => Close(QuoteStatus.Accepted, today, channel, response, now);

    /// <summary>The customer rejected it, while it is valid on the business's <paramref name="today"/>.</summary>
    public void Reject(DateOnly today, QuoteReplyChannel channel, string? response, DateTimeOffset now)
        => Close(QuoteStatus.Rejected, today, channel, response, now);

    /// <summary>
    /// The accepted version became the job (Fase 29E): the service order <paramref name="orderId"/>, once. What was paid for
    /// the visit and became a payment of that order (<paramref name="appliedVisitCredit"/>, zero when nothing did) stays as
    /// what it takes off: never more than what was fixed for the visit, nor than its total.
    /// </summary>
    public void ConvertToOrder(Guid orderId, decimal appliedVisitCredit, DateTimeOffset now)
    {
        if (Status != QuoteStatus.Accepted)
            throw new DomainException(QuoteRules.NotAcceptedMessage);
        if (OrderId is not null)
            throw new DomainException(QuoteRules.AlreadyConvertedMessage);
        if (orderId == Guid.Empty)
            throw new ArgumentException("The order is required.", nameof(orderId));
        if (appliedVisitCredit < 0 || appliedVisitCredit > (VisitCreditAmount ?? 0m) || appliedVisitCredit > Total)
            throw new ArgumentOutOfRangeException(nameof(appliedVisitCredit), "What it applies is what it takes off for the visit, at most.");

        OrderId = orderId;
        ConvertedAt = now;
        AppliedVisitCredit = appliedVisitCredit > 0 ? appliedVisitCredit : null;
        UpdatedAt = now;
    }

    private void Close(QuoteStatus status, DateOnly today, QuoteReplyChannel channel, string? response, DateTimeOffset now)
    {
        if (Status != QuoteStatus.Sent)
            throw new DomainException(QuoteRules.NotSentMessage);
        if (StatusOn(today) == QuoteStatus.Expired)
            throw new DomainException(QuoteRules.ExpiredMessage);
        if (!Enum.IsDefined(channel))
            throw new DomainException(QuoteRules.ReplyChannelMessage);
        var trimmed = string.IsNullOrWhiteSpace(response) ? null : response.Trim();
        if (trimmed?.Length > QuoteRules.MaxResponseLength)
            throw new DomainException(QuoteRules.ResponseTooLongMessage);

        Status = status;
        ReplyChannel = channel;
        CustomerResponse = trimmed;
        ClosedAt = now;
        UpdatedAt = now;
    }

    private void Supersede(DateTimeOffset now)
    {
        if (Status is not (QuoteStatus.Sent or QuoteStatus.Rejected))
            throw new DomainException(QuoteRules.NotReplaceableMessage);
        Status = QuoteStatus.Superseded;
        // A rejected one keeps when the customer answered.
        ClosedAt ??= now;
        UpdatedAt = now;
    }

    private void EnsureDraft()
    {
        if (Status != QuoteStatus.Draft)
            throw new DomainException(QuoteRules.NotDraftMessage);
    }

    private void SetCustomer(QuoteCustomer customer)
    {
        ArgumentNullException.ThrowIfNull(customer);
        CustomerId = customer.CustomerId;
        CustomerName = customer.Name;
        CustomerPhone = customer.Phone;
        CustomerEmail = customer.Email;
    }

    private void SetValidityDays(int days)
    {
        if (days is < QuoteRules.MinValidityDays or > QuoteRules.MaxValidityDays)
            throw new DomainException(QuoteRules.ValidityDaysMessage);
        ValidityDays = days;
    }

    private void SetNote(string? note)
    {
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed?.Length > QuoteRules.MaxNoteLength)
            throw new DomainException(QuoteRules.NoteTooLongMessage);
        Note = trimmed;
    }

    private void SetVisitCreditCore(QuoteVisitCredit? credit)
    {
        if (credit is not null && AppointmentId is null)
            throw new DomainException(QuoteRules.VisitCreditMessage);
        VisitCreditSaleOrderId = credit?.SaleOrderId;
        VisitCreditAmount = credit?.Amount;
    }

    private void RecalculateTotals()
    {
        var breakdown = GetTaxBreakdown();
        SubTotal = breakdown.Sum(line => line.TaxableBase);
        Tax = breakdown.Sum(line => line.TaxAmount);
        Total = SubTotal + Tax;
    }
}
