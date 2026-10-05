namespace FoodParkDigital.Application.Common.Diagnostics;

/// <summary>Values that are safe to write in any log.</summary>
public static class LogValues
{
    private const string Hidden = "***";

    /// <summary>Keeps the last four digits of full-length numbers: <c>+525512345678</c> becomes <c>***5678</c>.</summary>
    public static string Phone(string? phoneNumber)
    {
        var digits = new string((phoneNumber ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return digits.Length >= 8 ? Hidden + digits[^4..] : Hidden;
    }
}
