using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Services;

/// <summary>
/// Folios from the counters of each business (<see cref="FolioSequence"/>). In SQL Server one statement takes the
/// next number under the row's lock, so two users saving at the same time never get the same folio. Inside the
/// transaction of a document's request (<c>ITransactionalRequest</c>) the lock lasts until it commits and a refused
/// request gives the number back; outside one (supplies consumed by a status change) the number is committed right
/// away and a refusal afterwards leaves a gap. Other providers (tests) increment the tracked counter, saved with the
/// document.
/// </summary>
public sealed class FolioGenerator : IFolioGenerator
{
    private readonly ApplicationDbContext _context;

    public FolioGenerator(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<string> NextOrderNumberAsync(Guid tenantId, CancellationToken cancellationToken)
        => Folios.OrderNumber(await NextAsync(tenantId, Folios.OrderScope, FolioKind.Order, cancellationToken));

    public async Task<string> NextInventoryMovementNumberAsync(Guid tenantId, DateTimeOffset movementDate, CancellationToken cancellationToken)
    {
        var scope = Folios.InventoryMovementScope(movementDate);
        return Folios.DailyNumber(scope, await NextAsync(tenantId, scope, FolioKind.InventoryMovement, cancellationToken));
    }

    public async Task HoldInventoryMovementNumbersAsync(Guid tenantId, DateTimeOffset movementDate, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Un folio es de un negocio.", nameof(tenantId));
        // The same lock the next number takes, held to the end of the request's transaction; on a counter not used yet
        // that day, the range where it would go. Other providers (tests) have no concurrent requests to hold back.
        if (!_context.Database.IsSqlServer())
            return;

        var scope = Folios.InventoryMovementScope(movementDate);
        await _context.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM [FolioSequences] WITH (UPDLOCK, HOLDLOCK) WHERE [TenantId] = {tenantId} AND [Scope] = {scope};",
            cancellationToken);
    }

    public async Task<string> NextPurchaseOrderNumberAsync(Guid tenantId, DateTimeOffset orderedAt, CancellationToken cancellationToken)
    {
        var scope = Folios.PurchaseOrderScope(orderedAt);
        return Folios.DailyNumber(scope, await NextAsync(tenantId, scope, FolioKind.PurchaseOrder, cancellationToken));
    }

    public async Task<string> NextInventoryCountNumberAsync(Guid tenantId, DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        var scope = Folios.InventoryCountScope(startedAt);
        return Folios.DailyNumber(scope, await NextAsync(tenantId, scope, FolioKind.InventoryCount, cancellationToken));
    }

    public async Task<string> NextInventoryTransferNumberAsync(Guid tenantId, DateTimeOffset transferredAt, CancellationToken cancellationToken)
    {
        var scope = Folios.InventoryTransferScope(transferredAt);
        return Folios.DailyNumber(scope, await NextAsync(tenantId, scope, FolioKind.InventoryTransfer, cancellationToken));
    }

    private async Task<long> NextAsync(Guid tenantId, string scope, FolioKind kind, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Un folio es de un negocio.", nameof(tenantId));

        if (_context.Database.IsSqlServer())
            return await NextOnSqlServerAsync(tenantId, scope, kind, cancellationToken);

        // IgnoreQueryFilters: orders placed from the public menu have no business in context; filtered by tenant.
        var sequence = _context.FolioSequences.Local.FirstOrDefault(s => s.TenantId == tenantId && s.Scope == scope)
            ?? await _context.FolioSequences.IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Scope == scope, cancellationToken);
        if (sequence is null)
        {
            sequence = new FolioSequence(tenantId, scope, await LastGivenAsync(tenantId, scope, kind, cancellationToken));
            _context.FolioSequences.Add(sequence);
        }

        return sequence.Next();
    }

    private async Task<long> NextOnSqlServerAsync(Guid tenantId, string scope, FolioKind kind, CancellationToken cancellationToken)
    {
        if (await IncrementOnSqlServerAsync(tenantId, scope, cancellationToken) is { } number)
            return number;

        // A counter used for the first time (a new business, the first movement of a day): it starts after the folios
        // the business already has with that prefix. The range lock keeps two first documents from creating it twice.
        // XACT_ABORT undoes the batch if it is cut short (a timeout) instead of leaving it open with the lock. It lasts
        // only for this batch (sp_executesql restores it), so the request's own transaction keeps its error handling.
        await (kind switch
        {
            FolioKind.Order => _context.Database.ExecuteSqlAsync($"""
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                IF NOT EXISTS (SELECT 1 FROM [FolioSequences] WITH (UPDLOCK, HOLDLOCK) WHERE [TenantId] = {tenantId} AND [Scope] = {scope})
                    INSERT INTO [FolioSequences] ([TenantId], [Scope], [LastNumber])
                    SELECT {tenantId}, {scope}, ISNULL(MAX(TRY_CAST(SUBSTRING([OrderNumber], {Folios.OrderPrefix.Length + 1}, 18) AS bigint)), 0)
                    FROM [Orders]
                    WHERE [TenantId] = {tenantId} AND [OrderNumber] LIKE {Folios.OrderPrefix + "%"};
                COMMIT TRANSACTION;
                """, cancellationToken),
            FolioKind.InventoryMovement => _context.Database.ExecuteSqlAsync($"""
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                IF NOT EXISTS (SELECT 1 FROM [FolioSequences] WITH (UPDLOCK, HOLDLOCK) WHERE [TenantId] = {tenantId} AND [Scope] = {scope})
                    INSERT INTO [FolioSequences] ([TenantId], [Scope], [LastNumber])
                    SELECT {tenantId}, {scope}, ISNULL(MAX(TRY_CAST(SUBSTRING([MovementNumber], {scope.Length + 2}, 18) AS bigint)), 0)
                    FROM [InventoryMovements]
                    WHERE [TenantId] = {tenantId} AND [MovementNumber] LIKE {scope + "-%"};
                COMMIT TRANSACTION;
                """, cancellationToken),
            FolioKind.PurchaseOrder => _context.Database.ExecuteSqlAsync($"""
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                IF NOT EXISTS (SELECT 1 FROM [FolioSequences] WITH (UPDLOCK, HOLDLOCK) WHERE [TenantId] = {tenantId} AND [Scope] = {scope})
                    INSERT INTO [FolioSequences] ([TenantId], [Scope], [LastNumber])
                    SELECT {tenantId}, {scope}, ISNULL(MAX(TRY_CAST(SUBSTRING([OrderNumber], {scope.Length + 2}, 18) AS bigint)), 0)
                    FROM [PurchaseOrders]
                    WHERE [TenantId] = {tenantId} AND [OrderNumber] LIKE {scope + "-%"};
                COMMIT TRANSACTION;
                """, cancellationToken),
            FolioKind.InventoryCount => _context.Database.ExecuteSqlAsync($"""
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                IF NOT EXISTS (SELECT 1 FROM [FolioSequences] WITH (UPDLOCK, HOLDLOCK) WHERE [TenantId] = {tenantId} AND [Scope] = {scope})
                    INSERT INTO [FolioSequences] ([TenantId], [Scope], [LastNumber])
                    SELECT {tenantId}, {scope}, ISNULL(MAX(TRY_CAST(SUBSTRING([CountNumber], {scope.Length + 2}, 18) AS bigint)), 0)
                    FROM [InventoryCounts]
                    WHERE [TenantId] = {tenantId} AND [CountNumber] LIKE {scope + "-%"};
                COMMIT TRANSACTION;
                """, cancellationToken),
            FolioKind.InventoryTransfer => _context.Database.ExecuteSqlAsync($"""
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                IF NOT EXISTS (SELECT 1 FROM [FolioSequences] WITH (UPDLOCK, HOLDLOCK) WHERE [TenantId] = {tenantId} AND [Scope] = {scope})
                    INSERT INTO [FolioSequences] ([TenantId], [Scope], [LastNumber])
                    SELECT {tenantId}, {scope}, ISNULL(MAX(TRY_CAST(SUBSTRING([TransferNumber], {scope.Length + 2}, 18) AS bigint)), 0)
                    FROM [InventoryTransfers]
                    WHERE [TenantId] = {tenantId} AND [TransferNumber] LIKE {scope + "-%"};
                COMMIT TRANSACTION;
                """, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        });

        return await IncrementOnSqlServerAsync(tenantId, scope, cancellationToken)
            ?? throw new InvalidOperationException("The folio counter of the business could not be created.");
    }

    private async Task<long?> IncrementOnSqlServerAsync(Guid tenantId, string scope, CancellationToken cancellationToken)
    {
        var numbers = await _context.Database.SqlQuery<long>($"""
            UPDATE [FolioSequences] SET [LastNumber] = [LastNumber] + 1
            OUTPUT inserted.[LastNumber] AS [Value]
            WHERE [TenantId] = {tenantId} AND [Scope] = {scope}
            """).ToListAsync(cancellationToken);
        return numbers.Count == 0 ? null : numbers[0];
    }

    /// <summary>The highest folio the business already has with the counter's prefix (0 without any).</summary>
    private async Task<long> LastGivenAsync(Guid tenantId, string scope, FolioKind kind, CancellationToken cancellationToken)
    {
        var prefix = kind == FolioKind.Order ? Folios.OrderPrefix : scope + "-";
        var numbers = kind switch
        {
            FolioKind.Order => await _context.Orders.IgnoreQueryFilters()
                .Where(o => o.TenantId == tenantId && o.OrderNumber.StartsWith(prefix))
                .Select(o => o.OrderNumber)
                .ToListAsync(cancellationToken),
            FolioKind.InventoryMovement => await _context.InventoryMovements.IgnoreQueryFilters()
                .Where(m => m.TenantId == tenantId && m.MovementNumber.StartsWith(prefix))
                .Select(m => m.MovementNumber)
                .ToListAsync(cancellationToken),
            FolioKind.PurchaseOrder => await _context.PurchaseOrders.IgnoreQueryFilters()
                .Where(p => p.TenantId == tenantId && p.OrderNumber.StartsWith(prefix))
                .Select(p => p.OrderNumber)
                .ToListAsync(cancellationToken),
            FolioKind.InventoryCount => await _context.InventoryCounts.IgnoreQueryFilters()
                .Where(c => c.TenantId == tenantId && c.CountNumber.StartsWith(prefix))
                .Select(c => c.CountNumber)
                .ToListAsync(cancellationToken),
            FolioKind.InventoryTransfer => await _context.InventoryTransfers.IgnoreQueryFilters()
                .Where(t => t.TenantId == tenantId && t.TransferNumber.StartsWith(prefix))
                .Select(t => t.TransferNumber)
                .ToListAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        return numbers
            .Select(n => long.TryParse(n.AsSpan(prefix.Length), out var value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max();
    }

    private enum FolioKind
    {
        Order,
        InventoryMovement,
        PurchaseOrder,
        InventoryCount,
        InventoryTransfer
    }
}
