namespace FoodParkDigital.Domain.Messaging;

/// <summary>The key that identifies a recipient for opt-outs and duplicates.</summary>
public static class MessagingPhone
{
    public const int MaxLength = 20;

    /// <summary>
    /// WhatsApp reports Mexican mobiles as +521 and ten digits, while typed numbers are stored as +52 and ten
    /// digits: both are the same person, so the key drops that 1.
    /// </summary>
    public static string ToKey(string e164Phone)
    {
        var phone = e164Phone.Trim();
        return phone.Length == 14 && phone.StartsWith("+521", StringComparison.Ordinal)
            ? $"+52{phone[4..]}"
            : phone;
    }

    /// <summary>
    /// The forms the same number can be stored with (+52 and ten digits, or +521 as WhatsApp reports
    /// Mexican mobiles), to find a customer's orders and conversation whichever way each was saved.
    /// </summary>
    public static IReadOnlyList<string> Variants(string e164Phone)
    {
        var key = ToKey(e164Phone);
        return key.Length == 13 && key.StartsWith("+52", StringComparison.Ordinal)
            ? [key, $"+521{key[3..]}"]
            : [key];
    }
}
