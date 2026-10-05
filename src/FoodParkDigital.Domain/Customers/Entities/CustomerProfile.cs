namespace FoodParkDigital.Domain.Customers.Entities;

public class CustomerProfile
{
    private CustomerProfile() { }

    public CustomerProfile(Guid userId, string phoneNormalized, string? displayName, string? firstName, string? lastName)
    {
        UserId = userId;
        PhoneNormalized = phoneNormalized;
        DisplayName = displayName;
        FirstName = firstName;
        LastName = lastName;
        LoyaltyPoints = 0;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid UserId { get; private set; }
    public string PhoneNormalized { get; private set; } = null!;
    public string? DisplayName { get; private set; }
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    // Columns of the first model, shared by every business and never used: loyalty belongs to each business and is
    // out of scope until it has rules (docs/adr/0009-loyalty-per-business.md). Nothing writes them.
    public long LoyaltyPoints { get; private set; }
    public string? LoyaltyTier { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public void UpdateProfile(string phoneNormalized, string? displayName, string? firstName, string? lastName)
    {
        PhoneNormalized = phoneNormalized;
        DisplayName = displayName;
        FirstName = firstName;
        LastName = lastName;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
