using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;

namespace FoodParkDigital.Domain.Tests;

/// <summary>What a product and its options cost in the app of each delivery platform, apart from the menu.</summary>
public class ProductPlatformPriceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void PriceIn_ShouldBeTheMenuPrice_UntilThePlatformHasOneOfItsOwn()
    {
        var latte = NewLatte();

        Assert.Equal(55m, latte.PriceIn(OrderChannel.Rappi));

        latte.SetPlatformPrice(OrderChannel.Rappi, 69m);

        Assert.Equal(69m, latte.PriceIn(OrderChannel.Rappi));
        Assert.Equal(55m, latte.PriceIn(OrderChannel.UberEats));
        Assert.Equal(55m, latte.PriceIn(OrderChannel.DidiFood));
        var price = Assert.Single(latte.PlatformPrices);
        Assert.Equal((TenantId, latte.Id, OrderChannel.Rappi, 69m), (price.TenantId, price.ProductId, price.Channel, price.Price));
    }

    [Theory]
    [InlineData(OrderChannel.Counter)]
    [InlineData(OrderChannel.WhatsApp)]
    [InlineData(OrderChannel.Online)]
    public void PriceIn_ShouldKeepTheMenuPrice_OutsideTheDeliveryPlatforms(OrderChannel channel)
    {
        var latte = NewLatte();
        foreach (var platform in new[] { OrderChannel.Rappi, OrderChannel.UberEats, OrderChannel.DidiFood })
            latte.SetPlatformPrice(platform, 69m);
        latte.SetOptionPlatformPrice(Option(latte, "Grande").Id, OrderChannel.Rappi, 15m);

        Assert.Equal(55m, latte.PriceIn(channel));
        Assert.Equal(12m, Option(latte, "Grande").PriceDeltaIn(channel));
    }

    [Fact]
    public void SetPlatformPrice_ShouldChangeThePrice_AndNullShouldGoBackToTheMenu()
    {
        var latte = NewLatte();
        latte.SetPlatformPrice(OrderChannel.UberEats, 69m);

        latte.SetPlatformPrice(OrderChannel.UberEats, 72.5m);

        Assert.Equal(72.5m, Assert.Single(latte.PlatformPrices).Price);

        latte.SetPlatformPrice(OrderChannel.UberEats, null);
        latte.SetPlatformPrice(OrderChannel.DidiFood, null);

        Assert.Empty(latte.PlatformPrices);
        Assert.Equal(55m, latte.PriceIn(OrderChannel.UberEats));
    }

    [Theory]
    [InlineData(OrderChannel.Counter)]
    [InlineData(OrderChannel.WhatsApp)]
    [InlineData(OrderChannel.Online)]
    [InlineData((OrderChannel)9)]
    public void SetPlatformPrices_ShouldRefuseAChannelThatIsNotADeliveryPlatform(OrderChannel channel)
    {
        var latte = NewLatte();

        var product = Assert.Throws<DomainException>(() => latte.SetPlatformPrice(channel, 69m));
        var option = Assert.Throws<DomainException>(() => latte.SetOptionPlatformPrice(Option(latte, "Grande").Id, channel, 15m));

        Assert.Equal(DeliveryPlatformSetting.NotADeliveryPlatformMessage, product.Message);
        Assert.Equal(DeliveryPlatformSetting.NotADeliveryPlatformMessage, option.Message);
        Assert.Empty(latte.PlatformPrices);
        Assert.Empty(Option(latte, "Grande").PlatformPrices);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("69.005")]
    [InlineData("1000000.01")]
    public void SetPlatformPrice_ShouldRefuseAPriceAMenuCouldNotHave(string price)
    {
        var latte = NewLatte();

        var error = Assert.Throws<DomainException>(() => latte.SetPlatformPrice(OrderChannel.Rappi, decimal.Parse(price)));

        Assert.Contains("'Latte' en Rappi", error.Message);
        Assert.Empty(latte.PlatformPrices);
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("1000000")]
    public void SetPlatformPrice_ShouldTakeTheLimits(string price)
    {
        var latte = NewLatte();

        latte.SetPlatformPrice(OrderChannel.Rappi, decimal.Parse(price));

        Assert.Equal(decimal.Parse(price), latte.PriceIn(OrderChannel.Rappi));
    }

    [Fact]
    public void ResolveOptions_ShouldCopyWhatEachOptionAddsInThePlatform_AndPriceWithShouldStartFromItsPrice()
    {
        var latte = NewLatte();
        latte.SetPlatformPrice(OrderChannel.Rappi, 69m);
        latte.SetOptionPlatformPrice(Option(latte, "Grande").Id, OrderChannel.Rappi, 15m);
        IReadOnlyCollection<Guid> chosen = [Option(latte, "Grande").Id, Option(latte, "Almendra").Id];

        var inRappi = latte.ResolveOptions(chosen, OrderChannel.Rappi);
        var atTheCounter = latte.ResolveOptions(chosen);

        // Almendra has no price in Rappi: it adds what it adds in the menu.
        Assert.Equal([15m, 8m], inRappi.Select(option => option.PriceDelta));
        Assert.Equal(69m + 15m + 8m, latte.PriceWith(inRappi, OrderChannel.Rappi));
        Assert.Equal([12m, 8m], atTheCounter.Select(option => option.PriceDelta));
        Assert.Equal(55m + 12m + 8m, latte.PriceWith(atTheCounter));
        // Uber Eats has no prices of its own: the menu's.
        Assert.Equal(55m + 12m + 8m, latte.PriceWith(latte.ResolveOptions(chosen, OrderChannel.UberEats), OrderChannel.UberEats));
    }

    [Theory]
    [InlineData("-100000")]
    [InlineData("-7")]
    [InlineData("0")]
    [InlineData("100000")]
    public void SetOptionPlatformPrice_ShouldTakeWhatTheMenuTakes_CheaperVariantsIncluded(string priceDelta)
    {
        var latte = NewLatte();
        var chico = Option(latte, "Chico");

        latte.SetOptionPlatformPrice(chico.Id, OrderChannel.DidiFood, decimal.Parse(priceDelta));

        Assert.Equal(decimal.Parse(priceDelta), chico.PriceDeltaIn(OrderChannel.DidiFood));
        Assert.Equal(-5m, chico.PriceDeltaIn(OrderChannel.Rappi));
        var price = Assert.Single(chico.PlatformPrices);
        Assert.Equal((TenantId, chico.Id, OrderChannel.DidiFood), (price.TenantId, price.OptionId, price.Channel));
    }

    [Theory]
    [InlineData("100000.01")]
    [InlineData("-100000.01")]
    [InlineData("1.005")]
    public void SetOptionPlatformPrice_ShouldRefuseWhatTheMenuRefuses(string priceDelta)
    {
        var latte = NewLatte();

        var error = Assert.Throws<DomainException>(
            () => latte.SetOptionPlatformPrice(Option(latte, "Grande").Id, OrderChannel.Rappi, decimal.Parse(priceDelta)));

        Assert.Contains("'Grande' en Rappi", error.Message);
        Assert.Empty(Option(latte, "Grande").PlatformPrices);
    }

    [Fact]
    public void SetOptionPlatformPrice_ShouldChangeIt_AndNullShouldGoBackToTheMenu()
    {
        var latte = NewLatte();
        var grande = Option(latte, "Grande");
        latte.SetOptionPlatformPrice(grande.Id, OrderChannel.Rappi, 15m);

        latte.SetOptionPlatformPrice(grande.Id, OrderChannel.Rappi, 16m);

        Assert.Equal(16m, Assert.Single(grande.PlatformPrices).PriceDelta);

        latte.SetOptionPlatformPrice(grande.Id, OrderChannel.Rappi, null);

        Assert.Empty(grande.PlatformPrices);
        Assert.Equal(12m, grande.PriceDeltaIn(OrderChannel.Rappi));
    }

    [Fact]
    public void SetOptionPlatformPrice_ShouldRefuseAnOptionThatIsNotTheProducts()
    {
        var latte = NewLatte();
        var other = NewLatte();

        var error = Assert.Throws<DomainException>(
            () => latte.SetOptionPlatformPrice(Option(other, "Grande").Id, OrderChannel.Rappi, 15m));

        Assert.Equal("Una de las opciones de 'Latte' ya no existe. Recarga e intenta de nuevo.", error.Message);
        Assert.Empty(Option(other, "Grande").PlatformPrices);
    }

    [Fact]
    public void OptionGroups_ShouldKeepThePlatformPricesOfTheOptionsThatStay()
    {
        var latte = NewLatte();
        var size = latte.OptionGroups.Single(group => group.Name == "Tamaño");
        var grande = Option(latte, "Grande");
        latte.SetOptionPlatformPrice(grande.Id, OrderChannel.Rappi, 15m);

        // The editor sends the options it keeps with their ids: a rename keeps the option, and its price in the app.
        latte.SetOptionGroups(
        [
            new ProductOptionGroupDefinition(size.Id, "Tamaño", 1, 1,
            [
                new ProductOptionDefinition(grande.Id, "Grande (16 oz)", 12m),
                new ProductOptionDefinition(null, "Mediano", 6m)
            ])
        ]);

        var kept = Option(latte, "Grande (16 oz)");
        Assert.Same(grande, kept);
        Assert.Equal(15m, kept.PriceDeltaIn(OrderChannel.Rappi));
        Assert.Equal(6m, Option(latte, "Mediano").PriceDeltaIn(OrderChannel.Rappi));
    }

    [Fact]
    public void PriceWith_ShouldNeverGoBelowZero_WithThePricesOfTheApp()
    {
        var latte = NewLatte();
        latte.SetPlatformPrice(OrderChannel.Rappi, 5m);
        latte.SetOptionPlatformPrice(Option(latte, "Chico").Id, OrderChannel.Rappi, -6m);
        var chico = latte.ResolveOptions([Option(latte, "Chico").Id], OrderChannel.Rappi);

        Assert.Throws<DomainException>(() => latte.PriceWith(chico, OrderChannel.Rappi));
        Assert.Equal(55m - 5m, latte.PriceWith(latte.ResolveOptions([Option(latte, "Chico").Id])));
    }

    private static Product NewLatte()
    {
        var latte = new Product(TenantId, Guid.NewGuid(), "Latte", null, 55m, "MXN");
        latte.SetOptionGroups(
        [
            new ProductOptionGroupDefinition(null, "Tamaño", 1, 1,
            [
                new ProductOptionDefinition(null, "Chico", -5m),
                new ProductOptionDefinition(null, "Grande", 12m)
            ]),
            new ProductOptionGroupDefinition(null, "Leche", 0, 1,
            [
                new ProductOptionDefinition(null, "Deslactosada", 0m),
                new ProductOptionDefinition(null, "Almendra", 8m)
            ])
        ]);
        return latte;
    }

    private static ProductOption Option(Product product, string name)
        => product.OptionGroups.SelectMany(group => group.Options).Single(option => option.Name == name);
}
