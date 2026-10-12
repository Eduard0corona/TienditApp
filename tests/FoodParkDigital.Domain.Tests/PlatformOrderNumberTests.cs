using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Platforms;

namespace FoodParkDigital.Domain.Tests;

/// <summary>The folio of a delivery platform's app, always written the same way so the business can find it.</summary>
public class PlatformOrderNumberTests
{
    [Theory]
    [InlineData("  #bc 953 ", "BC953")] // Uber Eats, as read aloud.
    [InlineData("152390", "152390")] // Rappi.
    [InlineData("12", "12")] // DiDi's pickup number.
    [InlineData("#a-1 2", "A-12")]
    [InlineData("\tbc 953\n", "BC953")] // Tabs, non-breaking spaces and new lines count as spaces.
    [InlineData("1234567890123456789", "1234567890123456789")] // DiDi's long id.
    public void From_ShouldNormalizeTheFolio(string raw, string expected)
    {
        Assert.Equal(expected, PlatformOrderNumber.From(raw).Value);
        Assert.True(PlatformOrderNumber.TryNormalize(raw, out var value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void From_ShouldAcceptUpTo30Characters()
    {
        var longest = new string('9', PlatformOrderNumber.MaxLength);

        Assert.Equal(longest, PlatformOrderNumber.From(longest).Value);
        Assert.Equal(30, PlatformOrderNumber.MaxLength);
    }

    [Theory]
    [InlineData("ÁB1")] // Accents are not part of any app's folio.
    [InlineData("BC_953")]
    [InlineData("BC.953")]
    [InlineData("ORD/12")]
    [InlineData("##12")] // Only one leading # is dropped.
    [InlineData("12#")]
    [InlineData("#")]
    [InlineData("1234567890123456789012345678901")] // 31 characters.
    public void From_ShouldRefuseOtherCharactersOrLongerFolios(string raw)
    {
        var error = Assert.Throws<DomainException>(() => PlatformOrderNumber.From(raw));

        Assert.Equal(PlatformOrderNumber.InvalidMessage, error.Message);
        Assert.False(PlatformOrderNumber.TryNormalize(raw, out var value));
        Assert.Null(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void From_ShouldAskForTheFolioWhenItIsMissing(string? raw)
    {
        var error = Assert.Throws<DomainException>(() => PlatformOrderNumber.From(raw));

        Assert.Equal(PlatformOrderNumber.RequiredMessage, error.Message);
        Assert.False(PlatformOrderNumber.TryNormalize(raw, out _));
    }

    [Fact]
    public void TryNormalize_ShouldRefuseInputLongerThanItsLimit_EvenWithSpaces()
    {
        var spaced = "BC953" + new string(' ', PlatformOrderNumber.MaxInputLength);

        Assert.False(PlatformOrderNumber.TryNormalize(spaced, out _));
        Assert.True(PlatformOrderNumber.TryNormalize("BC953" + new string(' ', PlatformOrderNumber.MaxInputLength - 5), out _));
    }

    [Fact]
    public void SameFolio_ShouldBeEqual_HoweverItWasTyped()
    {
        Assert.Equal(PlatformOrderNumber.From("#bc953"), PlatformOrderNumber.From("BC 953"));
        Assert.NotEqual(PlatformOrderNumber.From("BC953"), PlatformOrderNumber.From("BC954"));
        Assert.Equal("BC953", PlatformOrderNumber.From("bc953").ToString());
    }

    [Theory]
    [InlineData("#bc 953", "BC953")]
    [InlineData("bc9", "BC9")] // A part of the folio still searches.
    [InlineData(" 15 23 90 ", "152390")]
    [InlineData("##12", "12")]
    [InlineData("Juan Pérez", "")] // A name with accents does not search folios.
    [InlineData("55 1234-5678", "551234-5678")]
    [InlineData("+52 55", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("#", "")]
    public void NormalizeForSearch_ShouldWriteTheTermAsAStoredFolio(string? term, string expected)
    {
        Assert.Equal(expected, PlatformOrderNumber.NormalizeForSearch(term));
    }
}
