using FluentValidation;

namespace FoodParkDigital.Application.Identity;

/// <summary>
/// What a password needs wherever one is chosen (registration, an invitation, a new or recovered password, an employee
/// created with one): at least <see cref="MinimumLength"/> characters with an uppercase, a lowercase and a number, and
/// none of the passwords people try first nor the person's own email or name. Identity checks all of it when the
/// password is set; the validators only reject what is too short before calling it.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 8;

    public static IRuleBuilderOptions<T, string> NewPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MinimumLength(MinimumLength);
}
