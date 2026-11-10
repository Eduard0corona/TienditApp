using FoodParkDigital.Domain.Catalog.Entities;

namespace FoodParkDigital.Domain.Tests;

/// <summary>Two names are the same category of a business whatever their capitals and spaces, but not their accents.</summary>
public class CategoryNameKeyTests
{
    [Theory]
    [InlineData("Bebidas", "bebidas")]
    [InlineData("Bebidas", "  BEBIDAS  ")]
    [InlineData("Bebidas frías", "bebidas   FRÍAS")]
    [InlineData("Bebidas frías", "Bebidas\tfrías")]
    [InlineData("Bebidas frías", "Bebidas\r\nfrías")]
    [InlineData("Bebidas frías", "Bebidas frías")]
    [InlineData("Ñoquis", "ñoquis")]
    public void NamesThatDifferInCapitalsOrSpaces_ShouldHaveTheSameKey(string name, string other)
    {
        Assert.Equal(Category.NameKey(name), Category.NameKey(other));
    }

    [Theory]
    [InlineData("Café", "Cafe")]
    [InlineData("Bebidas frías", "Bebidasfrías")]
    [InlineData("Bebida", "Bebidas")]
    public void NamesThatDifferInAccentsOrLetters_ShouldHaveDifferentKeys(string name, string other)
    {
        Assert.NotEqual(Category.NameKey(name), Category.NameKey(other));
    }

    [Fact]
    public void Key_ShouldBeTheNameInLowercase_WithOneSpaceBetweenWords()
    {
        Assert.Equal("bebidas frías", Category.NameKey("  Bebidas \t FRÍAS "));
        Assert.Equal(string.Empty, Category.NameKey("   "));
        Assert.Equal(string.Empty, Category.NameKey(null));
    }
}
