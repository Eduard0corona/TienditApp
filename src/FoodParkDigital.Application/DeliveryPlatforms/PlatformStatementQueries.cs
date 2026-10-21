using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Reports.ReadModels;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.DeliveryPlatforms;

/// <summary>One line of a platform's statement, as the person's file read it.</summary>
public record PlatformStatementLineRequest
{
    /// <summary>The line of the file it came in, from 1, so the person finds it.</summary>
    public int Row { get; init; }

    /// <summary>The folio of the order as the file wrote it; empty for a line that is not an order (a fee, a promotion).</summary>
    public string? Folio { get; init; }

    /// <summary>What the line pays the business: negative when the platform deducts it.</summary>
    public decimal Amount { get; init; }

    public string? Concept { get; init; }
}

/// <summary>
/// Importar el estado de cuenta (Fase 22H, ADR 0040): the lines of a platform's statement against its orders, by folio, for
/// a settlement of the period (the open one, or a new one): which orders the settlement takes, what each brings by the
/// statement and by TienditApp, and what the statement has that no order explains. It saves nothing: the person reviews
/// it and saves the settlement as always, which checks everything again.
/// </summary>
[RequiresTenantManager]
public record MatchPlatformStatementQuery : IRequest<PlatformStatementMatchDto>
{
    public const int MaxLines = 5_000;
    public const int MaxConceptLength = 200;

    public OrderChannel? Channel { get; init; }

    /// <summary>The open settlement it is for; none for a new one.</summary>
    public Guid? SettlementId { get; init; }

    public DateOnly? PeriodFrom { get; init; }
    public DateOnly? PeriodTo { get; init; }
    public List<PlatformStatementLineRequest> Lines { get; init; } = [];
}

public class MatchPlatformStatementQueryValidator : AbstractValidator<MatchPlatformStatementQuery>
{
    public const string NoLinesMessage = "El estado de cuenta no tiene renglones.";
    public const string TooManyLinesMessage = "Se importan hasta 5,000 renglones a la vez.";
    public const string MissingLineMessage = "Falta un renglón del estado de cuenta.";

    public MatchPlatformStatementQueryValidator()
    {
        RuleFor(x => x.Channel)
            .Must(channel => channel is { } value && OrderChannelRules.IsDeliveryPlatform(value))
            .WithMessage(DeliveryPlatformSetting.NotADeliveryPlatformMessage);
        RuleFor(x => x.PeriodFrom).Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(GetPendingPlatformOrdersQueryValidator.ChooseThePeriodMessage)
            .Must(from => from >= PlatformSettlement.EarliestPeriodFrom).WithMessage(PlatformSettlement.PeriodTooOldMessage);
        RuleFor(x => x.PeriodTo).NotNull().WithMessage(GetPendingPlatformOrdersQueryValidator.ChooseThePeriodMessage);
        RuleFor(x => x)
            .Must(x => x.PeriodFrom is null || x.PeriodTo is null || x.PeriodTo >= x.PeriodFrom)
            .WithMessage(PlatformSettlement.PeriodOrderMessage)
            .Must(x => x.PeriodFrom is null || x.PeriodTo is null
                || x.PeriodTo.Value.DayNumber - x.PeriodFrom.Value.DayNumber + 1 <= PlatformSettlement.MaxPeriodDays)
            .WithMessage(PlatformSettlement.PeriodTooLongMessage);
        RuleFor(x => x.Lines).Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(NoLinesMessage)
            .NotEmpty().WithMessage(NoLinesMessage)
            .Must(lines => lines.Count <= MatchPlatformStatementQuery.MaxLines).WithMessage(TooManyLinesMessage)
            .Must(lines => lines.All(line => line is not null)).WithMessage(MissingLineMessage);
        RuleForEach(x => x.Lines).Where(line => line is not null).ChildRules(line =>
        {
            line.RuleFor(l => l.Row).GreaterThan(0).WithMessage("Cada renglón dice en qué línea del archivo venía.");
            line.RuleFor(l => l.Amount)
                .Must(amount => Math.Abs(amount) <= PlatformSettlement.MaxDepositedAmount && decimal.Round(amount, 2) == amount)
                .WithMessage(l => $"El importe de la línea {l.Row} no es válido.");
            line.RuleFor(l => l.Folio)
                .Must(folio => folio is null || folio.Length <= PlatformOrderNumber.MaxInputLength)
                .WithMessage(l => $"El folio de la línea {l.Row} es demasiado largo.");
            line.RuleFor(l => l.Concept)
                .Must(concept => concept is null || concept.Length <= MatchPlatformStatementQuery.MaxConceptLength)
                .WithMessage(l => $"El concepto de la línea {l.Row} es demasiado largo.");
        });
    }
}

public class MatchPlatformStatementQueryHandler : IRequestHandler<MatchPlatformStatementQuery, PlatformStatementMatchDto>
{
    public const string AnotherPlatformMessage = "La liquidación es de otra plataforma.";
    public const string NotOpenMessage = "Solo se importa el estado de cuenta a una liquidación abierta.";

    /// <summary>The most orders an ambiguous folio lists.</summary>
    public const int MaxOrdersPerFolio = 10;

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public MatchPlatformStatementQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<PlatformStatementMatchDto> Handle(MatchPlatformStatementQuery request, CancellationToken cancellationToken)
    {
        // The validator asks for them; without them there is nothing to compare.
        if (request.Channel is not { } channel || request.PeriodFrom is not { } from || request.PeriodTo is not { } to)
            return new PlatformStatementMatchDto();

        if (request.SettlementId is { } settlementId)
        {
            var settlement = await _context.PlatformSettlements
                .AsNoTracking()
                .Where(s => s.Id == settlementId)
                .Select(s => new { s.Channel, s.Status })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new EntityNotFoundException(nameof(PlatformSettlement), settlementId);
            if (settlement.Channel != channel)
                throw new ValidationException([new ValidationFailure(nameof(request.SettlementId), AnotherPlatformMessage)]);
            if (settlement.Status != PlatformSettlementStatus.Open)
                throw new ValidationException([new ValidationFailure(nameof(request.SettlementId), NotOpenMessage)]);
        }

        var statement = StatementLines.Read(request.Lines);
        var zone = await PlatformSettlementReader.ZoneAsync(_context, _tenantService.TenantId, cancellationToken);
        var endUtc = DailySalesReadModel.EndUtc(to, zone);

        // Every order of the platform with one of the folios: Uber Eats and DiDi Food repeat folios, so a folio can be
        // several orders, of other weeks.
        var folios = statement.Folios.Select(folio => folio.Folio).ToList();
        var rows = folios.Count == 0
            ? []
            : await PlatformSettlementReader.Orders(_context)
                .Where(row => row.Channel == channel && folios.Contains(row.ExternalOrderNumber))
                .ToListAsync(cancellationToken);
        var orderIds = rows.Select(row => row.OrderId).ToList();
        var settledIn = orderIds.Count == 0
            ? []
            : await _context.PlatformSettlementOrders
                .AsNoTracking()
                .Where(line => line.IsActive && orderIds.Contains(line.OrderId))
                .ToDictionaryAsync(line => line.OrderId, line => line.SettlementId, cancellationToken);
        var byFolio = rows.ToLookup(row => row.ExternalOrderNumber, StringComparer.Ordinal);

        var result = new PlatformStatementMatchDto
        {
            Concepts = statement.Concepts,
            Unreadable = statement.Unreadable,
            StatementTotal = request.Lines.Sum(line => line.Amount),
            ConceptsTotal = statement.Concepts.Sum(concept => concept.Amount)
        };
        foreach (var folio in statement.Folios)
            result.Folios.Add(Match(folio, byFolio[folio.Folio], settledIn, request.SettlementId, endUtc));
        result.MatchedTotal = result.Folios.Where(Takes).Sum(match => match.StatementAmount);
        result.ExpectedTotal = result.Folios.Where(Takes).Sum(match => match.ExpectedNet ?? 0);
        result.UnmatchedTotal = result.Folios.Where(match => !Takes(match)).Sum(match => match.StatementAmount)
            + statement.Unreadable.Sum(line => line.Amount);
        return result;

        PlatformStatementFolioDto Match(
            StatementLines.FolioLines folio,
            IEnumerable<PlatformSettlementReader.PlatformOrderRow> candidates,
            IReadOnlyDictionary<Guid, Guid> settled,
            Guid? settlementId,
            DateTimeOffset periodEndUtc)
        {
            var newestFirst = candidates
                .OrderByDescending(row => row.RegisteredAt)
                .ThenByDescending(row => row.OrderId)
                .ToList();
            bool InThis(PlatformSettlementReader.PlatformOrderRow row)
                => settlementId is { } id && settled.TryGetValue(row.OrderId, out var other) && other == id;
            bool Elsewhere(PlatformSettlementReader.PlatformOrderRow row)
                => settled.TryGetValue(row.OrderId, out var other) && other != settlementId;
            // As a pending order: net paid and expected settlement not zero, and not rounded to zero either.
            bool Brings(PlatformSettlementReader.PlatformOrderRow row)
                => row.NetPaidAmount != 0 && row.ExpectedSettlement != 0 && row.ExpectedNet != 0;

            var match = new PlatformStatementFolioDto { Folio = folio.Folio, Rows = folio.Rows, StatementAmount = folio.Amount };
            var takable = newestFirst
                .Where(row => InThis(row) || (!Elsewhere(row) && Brings(row) && row.RegisteredAt <= periodEndUtc))
                .ToList();
            if (takable.Count == 1)
            {
                var order = takable[0];
                match.Status = InThis(order) ? PlatformStatementMatchStatus.InThisSettlement : PlatformStatementMatchStatus.Pending;
                match.Orders = [PlatformSettlementReader.ToDto(order, order.ExpectedNet, from, zone)];
                match.ExpectedNet = order.ExpectedNet;
                match.Difference = folio.Amount - order.ExpectedNet;
                return match;
            }
            if (takable.Count > 1)
            {
                match.Status = PlatformStatementMatchStatus.Ambiguous;
                match.Orders = takable
                    .Take(MaxOrdersPerFolio)
                    .Select(order => PlatformSettlementReader.ToDto(order, order.ExpectedNet, from, zone))
                    .ToList();
                return match;
            }

            // None the settlement can take: the most recent order that says why.
            var afterPeriod = newestFirst.FirstOrDefault(row => !Elsewhere(row) && Brings(row));
            var elsewhere = newestFirst.FirstOrDefault(Elsewhere);
            var (status, explaining) = afterPeriod is not null
                ? (PlatformStatementMatchStatus.AfterPeriod, afterPeriod)
                : elsewhere is not null
                    ? (PlatformStatementMatchStatus.Settled, elsewhere)
                    : newestFirst.Count > 0
                        ? (PlatformStatementMatchStatus.NothingExpected, newestFirst[0])
                        : (PlatformStatementMatchStatus.Unknown, null);
            match.Status = status;
            if (explaining is not null)
                match.Orders = [PlatformSettlementReader.ToDto(explaining, explaining.ExpectedNet, from, zone)];
            if (status == PlatformStatementMatchStatus.Settled && explaining is not null)
                match.SettlementId = settled[explaining.OrderId];
            return match;
        }
    }

    private static bool Takes(PlatformStatementFolioDto match)
        => match.Status is PlatformStatementMatchStatus.InThisSettlement or PlatformStatementMatchStatus.Pending;
}

/// <summary>The lines of a statement by folio, by concept the ones without one, and the ones whose folio cannot be read.</summary>
internal static class StatementLines
{
    internal sealed record FolioLines(string Folio, List<int> Rows, decimal Amount);

    internal sealed record Grouped(
        List<FolioLines> Folios,
        List<PlatformStatementConceptDto> Concepts,
        List<PlatformStatementUnreadableDto> Unreadable);

    /// <summary>
    /// Adds up the lines of each folio, as the platforms write the folio of an order on its own line and on the lines of
    /// what they deduct from it (a promotion, a claim); and the lines without a folio by concept, trimmed and without
    /// telling capitals apart. A folio without a letter or a digit («-», «—») is no folio.
    /// </summary>
    public static Grouped Read(IReadOnlyList<PlatformStatementLineRequest> lines)
    {
        var folios = new List<FolioLines>();
        var folioIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var concepts = new List<PlatformStatementConceptDto>();
        var conceptIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var unreadable = new List<PlatformStatementUnreadableDto>();

        foreach (var line in lines)
        {
            var concept = Clean(line.Concept);
            var raw = line.Folio?.Trim() ?? "";
            if (raw.Length > 0 && !raw.Any(char.IsLetterOrDigit))
                raw = "";
            if (raw.Length == 0)
            {
                if (!conceptIndex.TryGetValue(concept, out var at))
                {
                    at = concepts.Count;
                    conceptIndex[concept] = at;
                    concepts.Add(new PlatformStatementConceptDto { Concept = concept });
                }
                concepts[at].Rows.Add(line.Row);
                concepts[at].Amount += line.Amount;
                continue;
            }

            if (!PlatformOrderNumber.TryNormalize(raw, out var folio))
            {
                unreadable.Add(new PlatformStatementUnreadableDto
                {
                    Row = line.Row,
                    Folio = raw,
                    Concept = concept.Length > 0 ? concept : null,
                    Amount = line.Amount
                });
                continue;
            }

            if (folioIndex.TryGetValue(folio, out var index))
            {
                var known = folios[index];
                known.Rows.Add(line.Row);
                folios[index] = known with { Amount = known.Amount + line.Amount };
            }
            else
            {
                folioIndex[folio] = folios.Count;
                folios.Add(new FolioLines(folio, [line.Row], line.Amount));
            }
        }

        return new Grouped(folios, concepts, unreadable);
    }

    private static string Clean(string? concept)
        => string.Join(' ', (concept ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
