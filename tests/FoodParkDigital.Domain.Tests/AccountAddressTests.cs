using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.HomeDelivery;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The addresses of a customer account (Fase 24B): the rules of an order's address, a name on one line, at most ten per
/// account, the same place whatever its capitals and spaces, and the last one used first.
/// </summary>
public class AccountAddressTests
{
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 13, 25, 0, TimeSpan.Zero);

    [Fact]
    public void Create_ShouldKeepTheAddressAsAnOrderDoes()
    {
        var address = AccountAddress.Create(Account, "  Casa ", " 72000 ", "  Av. Juárez 120, Centro ", "  Portón verde ", addressesKept: 0);

        Assert.Equal(Account, address.UserId);
        Assert.Equal(("Casa", "72000", "Av. Juárez 120, Centro", "Portón verde"),
            (address.Label, address.PostalCode, address.AddressLine, address.References));
        Assert.Null(address.LastUsedAt);
        Assert.Equal(DeliveryAddress.From("72000", "Av. Juárez 120, Centro", "Portón verde"), address.ToDeliveryAddress());
    }

    [Fact]
    public void Create_ShouldNameIt_WhenTheCustomerDoesNot()
    {
        Assert.Equal(AccountAddress.DefaultLabel, AccountAddress.Create(Account, "   ", "72000", "Av. Juárez 120", null, 0).Label);
        Assert.Equal(AccountAddress.DefaultLabel, AccountAddress.Create(Account, null, "72000", "Av. Juárez 120", null, 0).Label);
    }

    [Fact]
    public void Create_ShouldKeepTheName_OnOneShortLine()
    {
        Assert.Equal("Casa de mi mamá", AccountAddress.Create(Account, "Casa\nde\tmi\u2028mamá\u0000", "72000", "Av. Juárez 120", null, 0).Label);

        var label = AccountAddress.Create(Account, new string('a', 70), "72000", "Av. Juárez 120", null, 0).Label;
        Assert.Equal(AccountAddress.MaxLabelLength, label.Length);
    }

    [Theory]
    [InlineData(null, "Av. Juárez 120", DeliveryAddress.PostalCodeRequiredMessage)]
    [InlineData("7200", "Av. Juárez 120", "«7200» no es un código postal: lleva 5 dígitos.")]
    [InlineData("72000", " ", DeliveryAddress.AddressLineRequiredMessage)]
    public void Create_ShouldSayWhatIsMissingOrWrong(string? postalCode, string addressLine, string message)
        => Assert.Equal(message, Assert.Throws<DomainException>(
            () => AccountAddress.Create(Account, "Casa", postalCode, addressLine, null, 0)).Message);

    [Fact]
    public void Create_ShouldRefuseAnEleventhAddress_AndAnAddressWithoutAccount()
    {
        Assert.NotNull(AccountAddress.Create(Account, "Casa", "72000", "Av. Juárez 120", null, AccountAddress.MaxPerAccount - 1));
        Assert.Equal(AccountAddress.TooManyMessage, Assert.Throws<DomainException>(
            () => AccountAddress.Create(Account, "Casa", "72000", "Av. Juárez 120", null, AccountAddress.MaxPerAccount)).Message);
        Assert.Equal(AccountAddress.AccountRequiredMessage, Assert.Throws<DomainException>(
            () => AccountAddress.Create(Guid.Empty, "Casa", "72000", "Av. Juárez 120", null, 0)).Message);
    }

    [Fact]
    public void Change_ShouldFollowTheSameRules_AndLeaveItAsItWasWhenRefused()
    {
        var address = AccountAddress.Create(Account, "Casa", "72000", "Av. Juárez 120", "Portón verde", 0);

        address.Change("Oficina", "72160", "Calle 5 de Mayo 8", null);
        Assert.Equal(("Oficina", "72160", "Calle 5 de Mayo 8", (string?)null),
            (address.Label, address.PostalCode, address.AddressLine, address.References));

        Assert.Throws<DomainException>(() => address.Change("Casa", "7200", "Av. Juárez 120", null));
        Assert.Equal(("Oficina", "72160", "Calle 5 de Mayo 8"), (address.Label, address.PostalCode, address.AddressLine));
    }

    [Fact]
    public void IsSamePlaceAs_ShouldCompareThePostalCodeAndTheLine_NotTheirCapitalsOrSpaces()
    {
        var address = AccountAddress.Create(Account, "Casa", "72000", "Av. Juárez 120, Centro", "Portón verde", 0);

        Assert.True(address.IsSamePlaceAs(DeliveryAddress.From("72000", "av.  JUÁREZ 120,   centro", "Otro portón")));
        Assert.False(address.IsSamePlaceAs(DeliveryAddress.From("72001", "Av. Juárez 120, Centro", null)));
        Assert.False(address.IsSamePlaceAs(DeliveryAddress.From("72000", "Av. Juárez 121, Centro", null)));
    }

    [Fact]
    public void MarkUsed_ShouldKeepTheReferencesOfTheOrder_OnlyForTheSamePlace()
    {
        var address = AccountAddress.Create(Account, "Casa", "72000", "Av. Juárez 120", "Portón verde", 0);

        address.MarkUsed(DeliveryAddress.From("72000", "AV. JUÁREZ 120", "Portón negro"), Now);
        Assert.Equal(("Portón negro", (DateTimeOffset?)Now), (address.References, address.LastUsedAt));
        Assert.Equal("Av. Juárez 120", address.AddressLine);

        Assert.Throws<DomainException>(() => address.MarkUsed(DeliveryAddress.From("72000", "Calle 5 de Mayo 8", null), Now.AddHours(1)));
        Assert.Equal(Now, address.LastUsedAt);
    }
}
