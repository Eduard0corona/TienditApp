using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Infrastructure.Identity;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>The passwords tried first when guessing are refused, whatever their letters' case.</summary>
public class GuessablePasswordValidatorTests
{
    private static readonly ApplicationUser Person = new()
    {
        Email = "mariana.lopez@demo.com",
        FirstName = "Mariana",
        LastName = "Lopez"
    };

    [Theory]
    [InlineData("Password1")]
    [InlineData("pASSWORD1")]
    [InlineData("Contraseña1")]
    [InlineData("Mexico2025")]
    public void CommonPasswords_ShouldBeRefused(string password)
    {
        Assert.True(GuessablePasswordValidator.IsGuessable(password, Person));
    }

    [Theory]
    // Their email, their name or the app's name inside the password.
    [InlineData("Mariana.lopez9")]
    [InlineData("Mariana2026")]
    [InlineData("SoyLopez88")]
    [InlineData("MiTienditapp1")]
    public void PersonalOrAppWords_ShouldBeRefused(string password)
    {
        Assert.True(GuessablePasswordValidator.IsGuessable(password, Person));
    }

    [Fact]
    public void OtherPasswords_ShouldPass_AndShortNamesShouldNotCount()
    {
        var shortName = new ApplicationUser { Email = "al@demo.com", FirstName = "Al", LastName = "Ro" };

        Assert.False(GuessablePasswordValidator.IsGuessable("Girasol-Azul27", Person));
        Assert.False(GuessablePasswordValidator.IsGuessable("Algarrobo71", shortName));
    }
}
