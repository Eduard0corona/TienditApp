using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Services;

/// <summary>
/// The booking lock of a business (Fase 24D) as an application lock of SQL Server owned by the request's transaction:
/// it is released when that transaction commits or rolls back. Waits up to 10 seconds; past them the request fails and
/// the customer tries again. Other providers (tests) have one request at a time and take no lock.
/// </summary>
public sealed class AppointmentBookingLock : IAppointmentBookingLock
{
    private const int TimeoutMilliseconds = 10_000;

    private readonly ApplicationDbContext _context;

    public AppointmentBookingLock(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AcquireAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!_context.Database.IsSqlServer())
            return;
        if (_context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("The booking lock lasts as long as the request's transaction: take it inside one.");

        var resource = $"appointments:{tenantId:N}";
        await _context.Database.ExecuteSqlAsync($"""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {TimeoutMilliseconds};
            IF @result < 0
                THROW 51000, N'The booking lock of the business was not granted.', 1;
            """, cancellationToken);
    }
}
