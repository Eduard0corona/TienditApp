using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Domain.Customers.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Services;

public class CustomerLookupService : ICustomerLookupService
{
    private readonly IApplicationDbContext _context;
    private readonly IPhoneNormalizationService _phoneNormalizationService;
    private readonly ILogger<CustomerLookupService> _logger;
    private readonly bool _logSensitiveData;

    public CustomerLookupService(
        IApplicationDbContext context,
        IPhoneNormalizationService phoneNormalizationService,
        IHostEnvironment environment,
        ILogger<CustomerLookupService> logger)
    {
        _context = context;
        _phoneNormalizationService = phoneNormalizationService;
        _logger = logger;
        // Customer phone numbers are only logged in full in Development.
        _logSensitiveData = environment.IsDevelopment();
    }

    public async Task<CustomerLookupResult> ResolveForOrderAsync(
        Guid tenantId,
        string rawPhone,
        string? name,
        string defaultCountry,
        CancellationToken cancellationToken)
    {
        var phoneNormalized = _phoneNormalizationService.NormalizeToE164(rawPhone, defaultCountry);

        var profile = await _context.CustomerProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PhoneNormalized == phoneNormalized, cancellationToken);

        if (profile is not null)
        {
            _logger.LogInformation(
                "OrderCustomerLinked: linked phone {Phone} to user {UserId}.",
                LoggablePhone(phoneNormalized),
                profile.UserId);
            return new CustomerLookupResult(phoneNormalized, profile.UserId, null, name);
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
                    LoggablePhone(phoneNormalized),
                    temp.Id);
            }
            catch (DbUpdateException)
            {
                temp = await _context.TemporaryCustomers
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PhoneNormalized == phoneNormalized, cancellationToken)
                    ?? throw new InvalidOperationException("Failed to create or retrieve temporary customer.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(temp.Name))
        {
            temp.UpdateName(name.Trim());
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new CustomerLookupResult(phoneNormalized, null, temp.Id, temp.Name ?? name);
    }

    private string LoggablePhone(string phoneNumber)
        => _logSensitiveData ? phoneNumber : LogRedaction.MaskPhoneNumber(phoneNumber);
}
