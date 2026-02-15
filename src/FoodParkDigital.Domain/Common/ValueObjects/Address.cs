namespace FoodParkDigital.Domain.Common.ValueObjects;

public class Address : ValueObject
{
    private Address() { }

    public Address(
        string street,
        string exteriorNumber,
        string? interiorNumber,
        string neighborhood,
        string zipCode,
        string city,
        string state,
        string country = "México")
    {
        Street = street;
        ExteriorNumber = exteriorNumber;
        InteriorNumber = interiorNumber;
        Neighborhood = neighborhood;
        ZipCode = zipCode;
        City = city;
        State = state;
        Country = country;
    }

    public string Street { get; private set; } = null!;
    public string ExteriorNumber { get; private set; } = null!;
    public string? InteriorNumber { get; private set; }
    public string Neighborhood { get; private set; } = null!;
    public string ZipCode { get; private set; } = null!;
    public string City { get; private set; } = null!;
    public string State { get; private set; } = null!;
    public string Country { get; private set; } = null!;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Street;
        yield return ExteriorNumber;
        yield return InteriorNumber;
        yield return Neighborhood;
        yield return ZipCode;
        yield return City;
        yield return State;
        yield return Country;
    }
}
