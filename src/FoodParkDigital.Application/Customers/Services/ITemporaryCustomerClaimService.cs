namespace FoodParkDigital.Application.Customers.Services;

public interface ITemporaryCustomerClaimService
{
    Task<int> ClaimByPhoneAsync(
        string rawPhone,
        Guid userId,
        string defaultCountry,
        CancellationToken cancellationToken);

    Task<int> ClaimByPhoneForTenantAsync(
        string rawPhone,
        Guid userId,
        Guid tenantId,
        string defaultCountry,
        CancellationToken cancellationToken);
}
