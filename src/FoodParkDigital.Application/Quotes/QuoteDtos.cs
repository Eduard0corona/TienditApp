using FoodParkDigital.Application.Appointments;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Quotes;

namespace FoodParkDigital.Application.Quotes;

/// <summary>A line of a quote as the business wrote it, with the amount the server worked out.</summary>
public class QuoteLineDto
{
    public int Position { get; set; }
    /// <summary>The service or product of the catalog it came from; null when written by hand or deleted since.</summary>
    public Guid? ProductId { get; set; }
    public string Description { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxRate { get; set; }
    public bool IsTaxExempt { get; set; }
    public decimal Total { get; set; }
}

/// <summary>The IVA of the lines of one rate.</summary>
public class QuoteTaxDto
{
    public decimal RatePercent { get; set; }
    public bool IsExempt { get; set; }
    public decimal TaxableBase { get; set; }
    public decimal TaxAmount { get; set; }
}

/// <summary>
/// What the customer paid for the visit, which the quote takes off (ADR 0053, «Se descuenta si contratas»): never more than
/// the sale of the visit still has paid. It stops applying once that sale is cancelled or all of it refunded; then
/// <see cref="Amount"/> is what it took off.
/// </summary>
public class QuoteVisitCreditDto
{
    public decimal Amount { get; set; }
    public bool Applies { get; set; }
}

/// <summary>A row of the list of quotes: one version, as it stands on the business's day.</summary>
public class QuoteSummaryDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = null!;
    public int Revision { get; set; }
    /// <summary>Its status today: a sent one whose validity passed reads <see cref="QuoteStatus.Expired"/>.</summary>
    public QuoteStatus Status { get; set; }
    public string CustomerName { get; set; } = null!;
    public string CustomerPhone { get; set; } = null!;
    public decimal Total { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool FromVisit { get; set; }
    /// <summary>The customer accepted or rejected it from its link (Fase 29G).</summary>
    public bool RepliedFromLink { get; set; }
}

/// <summary>Another version of the same quote, for its history.</summary>
public class QuoteVersionDto
{
    public Guid Id { get; set; }
    public int Revision { get; set; }
    public QuoteStatus Status { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}

/// <summary>The visit a quote comes from.</summary>
public class QuoteAppointmentDto
{
    public Guid Id { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public string ServiceName { get; set; } = null!;
    /// <summary>Where the visit was, by postal code; null for an appointment at the business.</summary>
    public string? PostalCode { get; set; }
    /// <summary>
    /// What happens, in the customer's words, when they requested the visit online (Fase 29H, ADR 0062); null for a visit the
    /// team arranged, whose notes are the team's.
    /// </summary>
    public string? Problem { get; set; }
    /// <summary>The photos of what happens the customer sent with it, as the private paths that open them, in the order sent.</summary>
    public List<string> ProblemPhotos { get; set; } = [];
}

/// <summary>A version of a quote, for the business (Fase 29C, ADR 0053).</summary>
public class QuoteDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = null!;
    public int Revision { get; set; }
    /// <summary>Its status today: a sent one whose validity passed reads <see cref="QuoteStatus.Expired"/>.</summary>
    public QuoteStatus Status { get; set; }
    public Guid? CustomerId { get; set; }
    public string CustomerName { get; set; } = null!;
    public string CustomerPhone { get; set; } = null!;
    public string? CustomerEmail { get; set; }
    public QuoteAppointmentDto? Appointment { get; set; }
    /// <summary>Whether its prices include IVA; when not, the IVA is added to them.</summary>
    public bool PricesIncludeTax { get; set; }
    public int ValidityDays { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public string? Note { get; set; }
    public List<QuoteLineDto> Lines { get; set; } = [];
    public decimal SubTotal { get; set; }
    public decimal Tax { get; set; }
    public List<QuoteTaxDto> Taxes { get; set; } = [];
    public decimal Total { get; set; }
    public QuoteVisitCreditDto? VisitCredit { get; set; }
    /// <summary>What is left to pay: the total less the visit while it applies.</summary>
    public decimal ToPay { get; set; }
    /// <summary>
    /// The deposit this version asks to accept it from its link (Fase 31N, ADR 0094), fixed when it was sent; null when it asks
    /// none. The business still records an acceptance itself without it.
    /// </summary>
    public decimal? DepositAmount { get; set; }
    /// <summary>The customer paid the deposit online, and so accepted it from its link: its order has it as «En línea».</summary>
    public bool DepositPaid { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    /// <summary>The link of this version (<c>/cotizacion#…</c>) once sent, while it opens.</summary>
    public string? TrackingToken { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public QuoteReplyChannel? ReplyChannel { get; set; }
    public string? CustomerResponse { get; set; }
    /// <summary>Its draft correction, when there is one and this is the version the customer has.</summary>
    public Guid? DraftRevisionId { get; set; }
    /// <summary>All the versions of the quote, from the first, this one included.</summary>
    public List<QuoteVersionDto> Versions { get; set; } = [];
    /// <summary>The latest emails of this version sent from TienditApp (Fase 29D), the newest first.</summary>
    public List<QuoteEmailDto> Emails { get; set; } = [];
    /// <summary>The latest WhatsApp messages of this version sent from TienditApp (Fase 29D), the newest first.</summary>
    public List<QuoteWhatsAppDto> WhatsApps { get; set; } = [];
    /// <summary>The business connected its WhatsApp: «Mandar por WhatsApp» goes from TienditApp, not from the phone.</summary>
    public bool WhatsAppConnected { get; set; }
    /// <summary>The service order this accepted version became (Fase 29E); null until it becomes one.</summary>
    public QuoteOrderDto? Order { get; set; }
    public DateTimeOffset? ConvertedAt { get; set; }
    /// <summary>
    /// It can become a service order now: accepted, not converted yet, in a business with service orders. Applying what was
    /// paid for the visit also needs who may charge, which the server checks.
    /// </summary>
    public bool CanConvertToOrder { get; set; }
    /// <summary>
    /// The job of the order it became can be agendado now (Fase 29F): converted, its order not cancelled, in a business with
    /// the agenda. Agendarlo also needs who may manage the agenda, which the server checks.
    /// </summary>
    public bool CanScheduleWork { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>The service order an accepted quote became (Fase 29E): its folio and where it is.</summary>
public class QuoteOrderDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = null!;
    public OrderStatus Status { get; set; }
    /// <summary>The appointments that do its job (Fase 29F), in the order of their time, cancelled ones included.</summary>
    public List<WorkAppointmentDto> WorkAppointments { get; set; } = [];
}

/// <summary>How the business's quotes are valid.</summary>
public class QuoteSettingsDto
{
    public int ValidityDays { get; set; }
    /// <summary>The customer accepts or rejects a quote from its link (Fase 29G, ADR 0061).</summary>
    public bool OnlineReplies { get; set; }
    /// <summary>
    /// The share of what is left to pay the customer pays online to accept a quote from its link (Fase 31N, ADR 0094), from
    /// 10 to 100; null when it asks none.
    /// </summary>
    public int? DepositPercent { get; set; }
    /// <summary>
    /// Whether the business can ask a deposit now: Mercado Pago connected, working and turned on, with service orders. Its
    /// customers answering from the link is <see cref="OnlineReplies"/>.
    /// </summary>
    public bool DepositAvailable { get; set; }
    public bool PricesIncludeTax { get; set; }
    public decimal DefaultTaxRate { get; set; }
    /// <summary>The business connected its WhatsApp (Fase 29D): its quotes go by WhatsApp from TienditApp.</summary>
    public bool WhatsAppConnected { get; set; }
    /// <summary>The template the quotes go by WhatsApp with, while it is active and for quotes; null for TienditApp's text.</summary>
    public Guid? WhatsAppTemplateId { get; set; }
    /// <summary>The templates it can pick: active ones, for quotes.</summary>
    public List<QuoteWhatsAppTemplateDto> WhatsAppTemplates { get; set; } = [];
}

/// <summary>Which quotes the list shows.</summary>
public enum QuoteListFilter
{
    /// <summary>Drafts and sent ones still valid: what is waiting on someone.</summary>
    Open = 0,
    Accepted = 1,
    Rejected = 2,
    Expired = 3,
    Discarded = 4,
    /// <summary>Every version but the replaced ones.</summary>
    All = 5,
    /// <summary>
    /// Accepted in the last 30 days and not yet their service order (Fase 29G): what «Requiere atención» counts
    /// (<see cref="QuotesToConvert"/>).
    /// </summary>
    ToConvert = 6
}

/// <summary>A line as the business writes it: from the catalog, or by hand with what it includes.</summary>
public class QuoteLineRequest
{
    public Guid? ProductId { get; set; }
    /// <summary>What it includes; for a catalog line, the product's name when empty.</summary>
    public string? Description { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>With or without IVA, as the quote's prices.</summary>
    public decimal UnitPrice { get; set; }
}
