namespace FoodParkDigital.Application.Customers.Services;

public interface ICustomerLookupService
{
    Task<CustomerLookupResult> ResolveForOrderAsync(
        Guid tenantId,
        string rawPhone,
        string? name,
        string defaultCountry,
        CancellationToken cancellationToken);
}

public record CustomerLookupResult(
    string PhoneNormalized,
    Guid? CustomerUserId,
    Guid? TemporaryCustomerId,
    string? ResolvedName);
