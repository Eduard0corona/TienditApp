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
public record PlatformStatementLineInput
{
    /// <summary>The line of the file it came in, from 1, so the person finds it.</summary>
    public int Row { get; init; }

    /// <summary>
    /// The folio of the order, or the platform's long id of it (Fase 22I), as the file wrote it; empty for a line that is not
    /// an order (a fee, a promotion).
    /// </summary>
    public string? Folio { get; init; }

    /// <summary>What the line pays the business: negative when the platform deducts it.</summary>
    public decimal Amount { get; init; }

    public string? Concept { get; init; }
}

/// <summary>
/// Importar el estado de cuenta (Fase 22H, ADR 0040): the lines of a platform's statement against its orders, by folio or
/// by the platform's long id of the order (Fase 22I, ADR 0041), for a settlement of the period (the open one, or a new one): which orders the settlement takes, what each brings by the
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
    public List<PlatformStatementLineInput> Lines { get; init; } = [];
}

public class MatchPlatformStatementQueryValidator : AbstractValidator<MatchPlatformStatementQuery>
{
    public const string NoLinesMessage = "El estado de cuenta no tiene renglones.";
    public const string TooManyLinesMessage = "Se importan hasta 5,000 renglones a la vez.";
    public const string MissingLineMessage = "Falta un renglón del estado de cuenta.";
    public const string RowMessage = "Cada renglón lleva la línea del archivo en que venía, desde 1.";

    public static string RepeatedRowMessage(int row) => $"La línea {row} del archivo viene dos veces.";
    public static string AmountMessage(int row) => $"El importe de la línea {row} va de -$10,000,000 a $10,000,000, con dos decimales.";
    public static string FolioTooLongMessage(int row) => $"El folio o ID del pedido de la línea {row} admite hasta 100 caracteres.";
    public static string ConceptTooLongMessage(int row) => $"El concepto de la línea {row} admite hasta 200 caracteres.";

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
            .Must(lines => lines.All(line => line is not null)).WithMessage(MissingLineMessage)
            .Must(lines => RepeatedRow(lines) is null).WithMessage(x => RepeatedRowMessage(RepeatedRow(x.Lines) ?? 0));
        // Each line is checked only within the limit: a body of a million wrong lines answers with one message, not millions.
        RuleForEach(x => x.Lines)
            .Where(line => line is not null)
            .ChildRules(line =>
            {
                line.RuleFor(l => l.Row).GreaterThan(0).WithMessage(RowMessage);
                line.RuleFor(l => l.Amount)
                    .Must(amount => Math.Abs(amount) <= PlatformSettlement.MaxDepositedAmount && decimal.Round(amount, 2) == amount)
                    .WithMessage(l => AmountMessage(l.Row));
                line.RuleFor(l => l.Folio)
                    .Must(folio => folio is null || folio.Length <= PlatformOrderId.MaxInputLength)
                    .WithMessage(l => FolioTooLongMessage(l.Row));
                line.RuleFor(l => l.Concept)
                    .Must(concept => concept is null || concept.Length <= MatchPlatformStatementQuery.MaxConceptLength)
                    .WithMessage(l => ConceptTooLongMessage(l.Row));
            })
            .When(x => x.Lines is not null && x.Lines.Count <= MatchPlatformStatementQuery.MaxLines);
    }

    /// <summary>The first line of the file that comes twice, if any.</summary>
    private static int? RepeatedRow(IEnumerable<PlatformStatementLineInput> lines)
    {
        var seen = new HashSet<int>();
        foreach (var line in lines)
        {
            if (line is not null && !seen.Add(line.Row))
                return line.Row;
        }
        return null;
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
    private readonly IDateTimeProvider _clock;

    public MatchPlatformStatementQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService, IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _clock = clock;
    }

    public async Task<PlatformStatementMatchDto> Handle(MatchPlatformStatementQuery request, CancellationToken cancellationToken)
    {
        // The validator asks for them; without them there is nothing to compare.
        if (request.Channel is not { } channel || request.PeriodFrom is not { } from || request.PeriodTo is not { } to)
            return new PlatformStatementMatchDto();

        var zone = await PlatformSettlementReader.ZoneAsync(_context, _tenantService.TenantId, cancellationToken);
        // What saving the settlement would refuse, refused before comparing: a period that ends after today (the
        // business's), and a new settlement while another of the platform is open.
        if (to > DailySalesReadModel.LocalToday(zone, _clock.UtcNow))
            throw new ValidationException([new ValidationFailure(nameof(request.PeriodTo), PlatformSettlement.PeriodInTheFutureMessage)]);
        var settlementId = request.SettlementId;
        if (settlementId is { } id)
        {
            var settlement = await _context.PlatformSettlements
                .AsNoTracking()
                .Where(s => s.Id == id)
                .Select(s => new { s.Channel, s.Status })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new EntityNotFoundException(nameof(PlatformSettlement), id);
            if (settlement.Channel != channel)
                throw new ValidationException([new ValidationFailure(nameof(request.SettlementId), AnotherPlatformMessage)]);
            if (settlement.Status != PlatformSettlementStatus.Open)
                throw new ValidationException([new ValidationFailure(nameof(request.SettlementId), NotOpenMessage)]);
        }
        else if (await _context.PlatformSettlements.AnyAsync(
                     s => s.Channel == channel && s.Status == PlatformSettlementStatus.Open, cancellationToken))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.SettlementId), PlatformSettlementErrors.OpenExists(channel))]);
        }

        var statement = StatementLines.Read(request.Lines);
        var endUtc = DailySalesReadModel.EndUtc(to, zone);
        // Each folio of the statement is the folio of an order in the app or its long id: an order is a candidate by either.
        var folios = statement.Folios.Select(folio => folio.Folio).ToList();
        var keys = folios.ToHashSet(StringComparer.Ordinal);
        var ofTheFolios = PlatformSettlementReader.Orders(_context)
            .Where(row => row.Channel == channel
                && (folios.Contains(row.ExternalOrderNumber) || (row.ExternalOrderId != null && folios.Contains(row.ExternalOrderId))));

        // The orders the settlement could take, of any date: its own and the pending ones (in no other settlement in force,
        // and bringing something), as the save reads them. Pending orders do not pile up: a settlement takes them.
        var own = folios.Count == 0 || settlementId is null
            ? []
            : await ofTheFolios
                .Where(row => _context.PlatformSettlementOrders.Any(line =>
                    line.OrderId == row.OrderId && line.IsActive && line.SettlementId == settlementId))
                .ToListAsync(cancellationToken);
        var pending = folios.Count == 0
            ? []
            : await PlatformSettlementReader.Pending(_context, settlementId)
                .Where(row => row.Channel == channel
                    && (folios.Contains(row.ExternalOrderNumber) || (row.ExternalOrderId != null && folios.Contains(row.ExternalOrderId))))
                .ToListAsync(cancellationToken);
        var ownIds = own.Select(row => row.OrderId).ToHashSet();
        var open = own.Concat(pending.Where(row => !ownIds.Contains(row.OrderId)))
            .SelectMany(row => KeysOf(row, keys).Select(key => (Key: key, Row: row)))
            .ToLookup(pair => pair.Key, pair => pair.Row, StringComparer.Ordinal);

        var result = new PlatformStatementMatchDto
        {
            Concepts = statement.Concepts,
            Unreadable = statement.Unreadable,
            StatementTotal = request.Lines.Sum(line => line.Amount),
            ConceptsTotal = statement.Concepts.Sum(concept => concept.Amount)
        };
        var unexplained = new List<PlatformStatementFolioDto>();
        // A statement that names an order by its folio on some lines and by its long id on others: one order, added once.
        var takenBy = new Dictionary<Guid, PlatformStatementFolioDto>();
        foreach (var folio in statement.Folios)
        {
            var match = Match(folio, open[folio.Folio]);
            if (Takes(match))
            {
                if (takenBy.TryGetValue(match.Orders[0].OrderId, out var same))
                {
                    same.Rows = same.Rows.Concat(match.Rows).Order().ToList();
                    same.StatementAmount += match.StatementAmount;
                    same.Difference = same.StatementAmount - same.ExpectedNet;
                    continue;
                }
                takenBy[match.Orders[0].OrderId] = match;
            }
            result.Folios.Add(match);
            if (match.Status == PlatformStatementMatchStatus.Unknown)
                unexplained.Add(match);
        }

        // A folio no order of the settlement can explain: its newest order in another settlement in force says why or, if
        // none, its newest order at all (it brings nothing); read only for those folios, one order each.
        if (unexplained.Count > 0)
        {
            var left = unexplained.Select(match => match.Folio).ToList();
            var settled = await NewestAsync(PlatformSettlementReader.Orders(_context).Where(row =>
                row.Channel == channel
                && _context.PlatformSettlementOrders.Any(line =>
                    line.OrderId == row.OrderId && line.IsActive && (settlementId == null || line.SettlementId != settlementId))),
                left, cancellationToken);
            var settledIds = settled.Values.Select(row => row.OrderId).ToList();
            var settledIn = settledIds.Count == 0
                ? []
                : await _context.PlatformSettlementOrders
                    .AsNoTracking()
                    .Where(line => line.IsActive && settledIds.Contains(line.OrderId))
                    .ToDictionaryAsync(line => line.OrderId, line => line.SettlementId, cancellationToken);
            var rest = left.Where(folio => !settled.ContainsKey(folio)).ToList();
            var any = rest.Count == 0
                ? []
                : await NewestAsync(PlatformSettlementReader.Orders(_context).Where(row => row.Channel == channel), rest, cancellationToken);
            foreach (var match in unexplained)
            {
                if (settled.TryGetValue(match.Folio, out var elsewhere))
                {
                    match.Status = PlatformStatementMatchStatus.Settled;
                    match.Orders = [Dto(elsewhere)];
                    match.SettlementId = settledIn.GetValueOrDefault(elsewhere.OrderId);
                }
                else if (any.TryGetValue(match.Folio, out var nothing))
                {
                    match.Status = PlatformStatementMatchStatus.NothingExpected;
                    match.Orders = [Dto(nothing)];
                }
            }
        }

        result.MatchedTotal = result.Folios.Where(Takes).Sum(match => match.StatementAmount);
        result.ExpectedTotal = result.Folios.Where(Takes).Sum(match => match.ExpectedNet ?? 0);
        result.UnmatchedTotal = result.Folios.Where(match => !Takes(match)).Sum(match => match.StatementAmount)
            + statement.Unreadable.Sum(line => line.Amount);
        return result;

        PlatformSettlementOrderDto Dto(PlatformSettlementReader.PlatformOrderRow row)
            => PlatformSettlementReader.ToDto(row, row.ExpectedNet, from, zone, to);

        // What a folio is among the orders the settlement could take; Unknown until the rest of its orders are read.
        PlatformStatementFolioDto Match(StatementLines.FolioLines folio, IEnumerable<PlatformSettlementReader.PlatformOrderRow> candidates)
        {
            var newestFirst = candidates
                .OrderByDescending(row => row.RegisteredAt)
                .ThenByDescending(row => row.OrderId)
                .ToList();
            bool Own(PlatformSettlementReader.PlatformOrderRow row) => ownIds.Contains(row.OrderId);
            // Its own orders stay even in 0 (given back after it took them); a pending one, rounded to 0, brings nothing.
            bool Takable(PlatformSettlementReader.PlatformOrderRow row)
                => row.RegisteredAt <= endUtc && (Own(row) || row.ExpectedNet != 0);

            var match = new PlatformStatementFolioDto
            {
                Folio = folio.Folio,
                Rows = folio.Rows,
                StatementAmount = folio.Amount,
                Status = PlatformStatementMatchStatus.Unknown
            };
            var takable = newestFirst.Where(Takable).ToList();
            // One of the folio already in the settlement is the one the person chose; with none, or several, the person picks.
            var chosen = takable.Where(Own).ToList();
            var single = chosen.Count == 1 ? chosen[0] : chosen.Count == 0 && takable.Count == 1 ? takable[0] : null;
            if (single is not null)
            {
                match.Status = Own(single) ? PlatformStatementMatchStatus.InThisSettlement : PlatformStatementMatchStatus.Pending;
                match.Orders = [Dto(single)];
                match.ExpectedNet = single.ExpectedNet;
                match.Difference = folio.Amount - single.ExpectedNet;
            }
            else if (takable.Count > 1)
            {
                match.Status = PlatformStatementMatchStatus.Ambiguous;
                match.Orders = takable.Take(MaxOrdersPerFolio).Select(Dto).ToList();
            }
            else if (newestFirst.FirstOrDefault(row => row.RegisteredAt > endUtc && row.ExpectedNet != 0) is { } later)
            {
                match.Status = PlatformStatementMatchStatus.AfterPeriod;
                match.Orders = [Dto(later)];
            }
            return match;
        }
    }

    /// <summary>
    /// The newest order among <paramref name="rows"/> of each of <paramref name="keys"/>, whose folio or long id it is: one row
    /// per folio read, not its history. A long id names one order (or the few captured twice), so its orders are read whole.
    /// </summary>
    private static async Task<Dictionary<string, PlatformSettlementReader.PlatformOrderRow>> NewestAsync(
        IQueryable<PlatformSettlementReader.PlatformOrderRow> rows,
        List<string> keys,
        CancellationToken cancellationToken)
    {
        var ofTheFolios = rows.Where(row => keys.Contains(row.ExternalOrderNumber));
        var newest = ofTheFolios
            .GroupBy(row => row.ExternalOrderNumber)
            .Select(group => new { Folio = group.Key, RegisteredAt = group.Max(row => row.RegisteredAt) });
        var byFolio = await (
                from row in ofTheFolios
                join last in newest on new { Folio = row.ExternalOrderNumber, row.RegisteredAt } equals new { last.Folio, last.RegisteredAt }
                select row)
            .ToListAsync(cancellationToken);
        var byLongId = await rows
            .Where(row => row.ExternalOrderId != null && keys.Contains(row.ExternalOrderId))
            .ToListAsync(cancellationToken);
        // Two orders of a folio registered at the same instant: the same one every time.
        return byFolio.Select(row => (Key: row.ExternalOrderNumber, Row: row))
            .Concat(byLongId.Select(row => (Key: row.ExternalOrderId!, Row: row)))
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(pair => pair.Row).MaxBy(row => (row.RegisteredAt, row.OrderId))!,
                StringComparer.Ordinal);
    }

    /// <summary>The folios of the statement an order answers to: its folio in the app, its long id, or both.</summary>
    private static IEnumerable<string> KeysOf(PlatformSettlementReader.PlatformOrderRow row, HashSet<string> keys)
    {
        if (keys.Contains(row.ExternalOrderNumber))
            yield return row.ExternalOrderNumber;
        if (row.ExternalOrderId is { } longId && longId != row.ExternalOrderNumber && keys.Contains(longId))
            yield return longId;
    }

    private static bool Takes(PlatformStatementFolioDto match)
        => match.Status is PlatformStatementMatchStatus.InThisSettlement or PlatformStatementMatchStatus.Pending;
}

/// <summary>
/// The lines of a statement by folio (or long id), by concept the ones without one, and the ones whose folio cannot be read.
/// </summary>
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
    /// telling capitals apart. A folio without a letter or a digit («-», «—») is no folio. Each folio may also be the
    /// platform's long id of the order (Fase 22I), written the same way; a text too long for a folio can only be one.
    /// </summary>
    public static Grouped Read(IReadOnlyList<PlatformStatementLineInput> lines)
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

            if (Key(raw) is not { } folio)
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

    /// <summary>The folio as the orders keep it, or the long id when it can only be one; null when it is neither.</summary>
    private static string? Key(string raw)
        => PlatformOrderNumber.TryNormalize(raw, out var folio) ? folio
            : PlatformOrderId.TryNormalize(raw, out var longId) ? longId
            : null;

    private static string Clean(string? concept)
        => string.Join(' ', (concept ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
