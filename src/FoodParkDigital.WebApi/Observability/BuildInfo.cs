using System.Reflection;

namespace FoodParkDigital.WebApi.Observability;

/// <summary>
/// The commit this API was built from. The SDK appends it to the informational version ("1.0.0+&lt;sha&gt;"): the one
/// git checked out, or the <c>SourceRevisionId</c> the deployment passes (the commit it publishes). The health
/// endpoints report it, so the deployment's smoke test knows which version answered even when the client did not
/// change.
/// </summary>
public static class BuildInfo
{
    /// <summary>The commit of this build, or null if it was built without one.</summary>
    public static string? Commit { get; } = CommitOf(
        typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>What follows the '+' of an informational version, or null when nothing does.</summary>
    public static string? CommitOf(string? informationalVersion)
    {
        var plus = informationalVersion?.IndexOf('+') ?? -1;
        return plus < 0 || plus == informationalVersion!.Length - 1 ? null : informationalVersion[(plus + 1)..];
    }
}
