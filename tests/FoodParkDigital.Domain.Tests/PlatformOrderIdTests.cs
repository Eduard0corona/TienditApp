using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Platforms;

namespace FoodParkDigital.Domain.Tests;

/// <summary>The long id a delivery platform gives an order (Fase 22I), written the same way as the folio.</summary>
public class PlatformOrderIdTests
{
    [Theory]
    [InlineData("3f9c2b1e-8d4a-4c6f-9b2e-1a7d5e0c3b84", "3F9C2B1E-8D4A-4C6F-9B2E-1A7D5E0C3B84")] // Uber Eats' UUID.
    [InlineData("1234567890123456789", "1234567890123456789")] // DiDi.
    [InlineData(" #2019 3456 78 ", "2019345678")] // Rappi, with spaces between groups.
    [InlineData("abcdef", "ABCDEF")] // Six characters at least.
    public void FromOptional_ShouldNormalizeTheLongId(string raw, string expected)
    {
        Assert.Equal(expected, PlatformOrderId.FromOptional(raw)!.Value);
        Assert.True(PlatformOrderId.TryNormalize(raw, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromOptional_ShouldLeaveAnEmptyBoxWithoutId(string? raw)
        => Assert.Null(PlatformOrderId.FromOptional(raw));

    [Theory]
    [InlineData("BC953")] // Uber Eats' five characters, in the wrong box.
    [InlineData("3f9c2b1e_8d4a")]
    [InlineData("ÁBCDEF1")]
    [InlineData("##123456")]
    public void FromOptional_ShouldRefuseWhatIsNotALongId(string raw)
    {
        Assert.Equal(PlatformOrderId.InvalidMessage, Assert.Throws<DomainException>(() => PlatformOrderId.FromOptional(raw)).Message);
        Assert.False(PlatformOrderId.TryNormalize(raw, out _));
    }

    [Fact]
    public void FromOptional_ShouldAcceptUpTo64Characters_ReadingUpTo100()
    {
        var longest = new string('9', PlatformOrderId.MaxLength);
        Assert.Equal(longest, PlatformOrderId.FromOptional(longest)!.Value);
        Assert.False(PlatformOrderId.TryNormalize(longest + "9", out _));
        // Spaces count toward what is read, not toward the id.
        Assert.True(PlatformOrderId.TryNormalize(string.Join(' ', Enumerable.Repeat("12345", 12)) + new string(' ', 25), out var spaced));
        Assert.Equal(60, spaced.Length);
        Assert.False(PlatformOrderId.TryNormalize(new string(' ', 95) + "123456", out _));
    }

    [Theory]
    [InlineData("8475", "8475")] // The last digits of DiDi's id.
    [InlineData(" #3f9c 2b1e-8d4a ", "3F9C2B1E-8D4A")]
    [InlineData("ab", "AB")]
    [InlineData("", "")]
    [InlineData("Juan Pérez", "")] // A name is not part of an id.
    [InlineData("3f9c_2b1e", "")]
    public void NormalizeForSearch_ShouldWriteATermAsTheStoredIds(string term, string expected)
        => Assert.Equal(expected, PlatformOrderId.NormalizeForSearch(term));

    [Fact]
    public void NormalizeForSearch_ShouldIgnoreTermsLongerThanAnId()
    {
        Assert.Equal(new string('9', 64), PlatformOrderId.NormalizeForSearch(new string('9', 64)));
        Assert.Equal("", PlatformOrderId.NormalizeForSearch(new string('9', 65)));
        Assert.Equal("", PlatformOrderId.NormalizeForSearch(new string('9', 101)));
    }
}
