using FoodParkDigital.Infrastructure.Services;

namespace FoodParkDigital.Infrastructure.BackgroundJobs;

internal static class FailureDescription
{
    private const int MaxLength = 1000;

    /// <summary>
    /// Text stored with a failed item. Database messages can quote row values (phones, names), so a chain
    /// with a database exception keeps only the exception types.
    /// </summary>
    public static string Describe(Exception exception)
    {
        var safe = LogRedaction.RedactDatabaseMessages(exception);
        var text = $"{safe.GetType().Name}: {safe.Message}";
        return text.Length <= MaxLength ? text : text[..MaxLength];
    }
}
