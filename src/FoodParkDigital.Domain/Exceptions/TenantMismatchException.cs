namespace FoodParkDigital.Domain.Exceptions;

public class TenantMismatchException : DomainException
{
    public TenantMismatchException()
        : base("Access denied. The resource does not belong to the current tenant.") { }

    public TenantMismatchException(string entityName, Guid entityId)
        : base($"Access denied. The {entityName} with ID '{entityId}' does not belong to the current tenant.") { }
}
