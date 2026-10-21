using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Services;

/// <summary>
/// The lock of a location (counts since Fase 27A, transfers since Fase 27B) as an application lock of SQL Server owned by the
/// request's transaction, like the credit lock: it is released when that transaction commits or rolls back. Waits up to 10
/// seconds; past them the request fails and the person tries again. Other providers (tests) have one request at a time and
/// take no lock.
/// </summary>
public sealed class InventoryLocationLock : IInventoryLocationLock
{
    private const int TimeoutMilliseconds = 10_000;

    private readonly ApplicationDbContext _context;

    public InventoryLocationLock(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AcquireAsync(Guid tenantId, Guid inventoryLocationId, CancellationToken cancellationToken)
    {
        if (!_context.Database.IsSqlServer())
            return;
        if (_context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("The location lock lasts as long as the request's transaction: take it inside one.");

        // The resource keeps its name of the 27A: while a swap runs both versions, a count on either waits for the other.
        var resource = $"inventory-count:{tenantId:N}:{inventoryLocationId:N}";
        await _context.Database.ExecuteSqlAsync($"""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {TimeoutMilliseconds};
            IF @result < 0
                THROW 51000, N'The lock of the location was not granted.', 1;
            """, cancellationToken);
    }
}
