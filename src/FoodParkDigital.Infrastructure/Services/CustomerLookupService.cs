using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Domain.Customers.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Services;

public class CustomerLookupService : ICustomerLookupService
{
    private readonly IApplicationDbContext _context;
    private readonly IPhoneNormalizationService _phoneNormalizationService;
    private readonly ILogger<CustomerLookupService> _logger;

    public CustomerLookupService(
        IApplicationDbContext context,
        IPhoneNormalizationService phoneNormalizationService,
        ILogger<CustomerLookupService> logger)
    {
        _context = context;
        _phoneNormalizationService = phoneNormalizationService;
        _logger = logger;
    }

    public async Task<CustomerLookupResult> ResolveForOrderAsync(
        Guid tenantId,
        string rawPhone,
        string? name,
        string defaultCountry,
        CancellationToken cancellationToken)
    {
        var phoneNormalized = _phoneNormalizationService.NormalizeToE164(rawPhone, defaultCountry);

        // Only an account that confirmed this number by SMS receives the order. Anyone can register a
        // profile with someone else's number, so an unverified one falls through to a temporary
        // customer, whose orders are claimed by whoever later confirms the number.
        var profile = await CustomerPhoneOwnershipRules.FindHolderAsync(
            _context,
            _phoneNormalizationService,
            phoneNormalized,
            cancellationToken);

        if (profile is { IsVerified: true })
        {
            _logger.LogInformation(
                "OrderCustomerLinked: linked phone {Phone} to user {UserId}.",
                phoneNormalized,
                profile.UserId);
            return new CustomerLookupResult(phoneNormalized, profile.UserId, null, name);
        }

        if (profile is not null)
        {
            _logger.LogInformation(
                "UnverifiedCustomerProfileSkipped: user {UserId} has not confirmed this phone; using a temporary customer.",
                profile.UserId);
        }

        var temp = await _context.TemporaryCustomers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PhoneNormalized == phoneNormalized, cancellationToken);

        if (temp is null)
        {
            temp = new TemporaryCustomer(tenantId, phoneNormalized, string.IsNullOrWhiteSpace(name) ? null : name.Trim());
            _context.TemporaryCustomers.Add(temp);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation(
                    "CreatedTempCustomer: tenant {TenantId}, phone {Phone}, tempCustomer {TemporaryCustomerId}.",
                    tenantId,
                    phoneNormalized,
                    temp.Id);
            }
            catch (DbUpdateException ex)
            {
                // A concurrent request created it first. Stop tracking the failed insert so the
                // caller's next SaveChanges (the order) does not try it again, then reuse the winner.
                _context.TemporaryCustomers.Entry(temp).State = EntityState.Detached;

                temp = await _context.TemporaryCustomers
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PhoneNormalized == phoneNormalized, cancellationToken)
                    ?? throw new InvalidOperationException("Failed to create or retrieve temporary customer.", ex);
            }
        }
        else if (!string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(temp.Name))
        {
            temp.UpdateName(name.Trim());
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new CustomerLookupResult(phoneNormalized, null, temp.Id, temp.Name ?? name);
    }
}
