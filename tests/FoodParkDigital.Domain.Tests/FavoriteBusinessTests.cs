using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Tests;

/// <summary>The businesses a customer account keeps among its favorites (Fase 24F): an account, a business, at most 100.</summary>
public class FavoriteBusinessTests
{
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly Guid Business = Guid.NewGuid();

    [Fact]
    public void Create_ShouldKeepTheBusinessForTheAccount()
    {
        var favorite = FavoriteBusiness.Create(Account, Business, favoritesKept: 0);

        Assert.Equal(Account, favorite.UserId);
        Assert.Equal(Business, favorite.BusinessId);
    }

    [Fact]
    public void Create_ShouldNeedAnAccountAndABusiness()
    {
        Assert.Equal(FavoriteBusiness.AccountRequiredMessage,
            Assert.Throws<DomainException>(() => FavoriteBusiness.Create(Guid.Empty, Business, 0)).Message);
        Assert.Equal(FavoriteBusiness.BusinessRequiredMessage,
            Assert.Throws<DomainException>(() => FavoriteBusiness.Create(Account, Guid.Empty, 0)).Message);
    }

    [Fact]
    public void Create_ShouldStopAtTheMostAnAccountKeeps()
    {
        Assert.NotNull(FavoriteBusiness.Create(Account, Business, FavoriteBusiness.MaxPerAccount - 1));

        Assert.Equal(FavoriteBusiness.TooManyMessage,
            Assert.Throws<DomainException>(() => FavoriteBusiness.Create(Account, Business, FavoriteBusiness.MaxPerAccount)).Message);
    }
}
