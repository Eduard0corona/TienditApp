using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Customers.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Customers.Services;

/// <summary>
/// When someone confirms a phone number, the temporary customers of that number (in one business or in all) become
/// theirs, and so do the orders placed with it.
/// </summary>
public class TemporaryCustomerClaimService : ITemporaryCustomerClaimService
{
    private readonly IApplicationDbContext _context;
    private readonly IPhoneNormalizationService _phoneNormalizationService;
    private readonly ILogger<TemporaryCustomerClaimService> _logger;

    public TemporaryCustomerClaimService(
        IApplicationDbContext context,
        IPhoneNormalizationService phoneNormalizationService,
        ILogger<TemporaryCustomerClaimService> logger)
    {
        _context = context;
        _phoneNormalizationService = phoneNormalizationService;
        _logger = logger;
    }

    public Task<int> ClaimByPhoneAsync(
        string rawPhone,
        Guid userId,
        string defaultCountry,
        CancellationToken cancellationToken)
    {
        return ClaimInternalAsync(rawPhone, userId, null, defaultCountry, cancellationToken);
    }

    public Task<int> ClaimByPhoneForTenantAsync(
        string rawPhone,
        Guid userId,
        Guid tenantId,
        string defaultCountry,
        CancellationToken cancellationToken)
    {
        return ClaimInternalAsync(rawPhone, userId, tenantId, defaultCountry, cancellationToken);
    }

    private async Task<int> ClaimInternalAsync(
        string rawPhone,
        Guid userId,
        Guid? tenantId,
        string defaultCountry,
        CancellationToken cancellationToken)
    {
        // No explicit transaction: the single SaveChanges below is atomic on its own, runs under the
        // retrying execution strategy, and joins the caller's transaction when there is one.
        var phoneNormalized = _phoneNormalizationService.NormalizeToE164(rawPhone, defaultCountry);

        var query = _context.TemporaryCustomers
            .IgnoreQueryFilters()
            .Where(x => x.PhoneNormalized == phoneNormalized);

        if (tenantId.HasValue)
            query = query.Where(x => x.TenantId == tenantId.Value);

        var tempCustomers = await query.ToListAsync(cancellationToken);
        if (tempCustomers.Count == 0)
            return 0;

        var tempCustomerIds = tempCustomers.Select(x => x.Id).ToList();
        var linkedOrders = await _context.Orders
            .IgnoreQueryFilters()
            .Where(x => x.TemporaryCustomerId.HasValue && tempCustomerIds.Contains(x.TemporaryCustomerId.Value))
            .ToListAsync(cancellationToken);

        foreach (var temp in tempCustomers)
        {
            if (temp.Status != TemporaryCustomerStatus.Claimed || temp.ClaimedUserId != userId)
            {
                temp.Claim(userId);
                _logger.LogInformation(
                    "ClaimedTempCustomer: tenant {TenantId}, tempCustomer {TemporaryCustomerId}, user {UserId}.",
                    temp.TenantId,
                    temp.Id,
                    userId);
            }
        }

        foreach (var order in linkedOrders)
            order.LinkToCustomerUser(userId);

        await _context.SaveChangesAsync(cancellationToken);
        return tempCustomers.Count;
    }
}
