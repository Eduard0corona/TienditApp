namespace FoodParkDigital.Infrastructure.Services;

/// <summary>Moves files that became private out of the public storage they were uploaded to.</summary>
public interface ILegacyPrivateFileMover
{
    /// <param name="isPrivateKey">Which keys of the public storage belong in the private one.</param>
    /// <returns>How many files moved.</returns>
    Task<int> MoveLegacyFilesAsync(Func<string, bool> isPrivateKey, CancellationToken cancellationToken);
}
