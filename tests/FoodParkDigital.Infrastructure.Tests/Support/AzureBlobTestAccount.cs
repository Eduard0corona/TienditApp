namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// Runs a test against a real Blob Storage endpoint only when <see cref="AzureBlobTestAccount.ConnectionVariable"/>
/// names one (for example Azurite): container access levels and conditional writes cannot be proven without it.
/// </summary>
public sealed class AzureBlobFactAttribute : FactAttribute
{
    public AzureBlobFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(AzureBlobTestAccount.ConnectionString))
            Skip = $"Define {AzureBlobTestAccount.ConnectionVariable} (a Blob Storage connection string) to run it.";
    }
}

public static class AzureBlobTestAccount
{
    public const string ConnectionVariable = "TIENDITAPP_TEST_AZURE_BLOB";

    public static string? ConnectionString => Environment.GetEnvironmentVariable(ConnectionVariable);
}
