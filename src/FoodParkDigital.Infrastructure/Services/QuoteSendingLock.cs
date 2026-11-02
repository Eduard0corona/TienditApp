using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Services;

/// <summary>
/// The lock of a business's quote sends (Fase 29D) as an application lock of SQL Server owned by the request's transaction,
/// like the invoicing lock: it is released when that transaction commits or rolls back. Waits up to 10 seconds; past them the
/// request fails and the person tries again. Other providers (tests) have one request at a time and take no lock.
/// </summary>
public sealed class QuoteSendingLock : IQuoteSendingLock
{
    private const int TimeoutMilliseconds = 10_000;

    private readonly ApplicationDbContext _context;

    public QuoteSendingLock(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AcquireAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!_context.Database.IsSqlServer())
            return;
        if (_context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("The lock of quote sends lasts as long as the request's transaction: take it inside one.");

        var resource = $"quote-sending:{tenantId:N}";
        await _context.Database.ExecuteSqlAsync($"""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {TimeoutMilliseconds};
            IF @result < 0
                THROW 51000, N'The lock of the business''s quote sends was not granted.', 1;
            """, cancellationToken);
    }
}
