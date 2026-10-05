namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>The public address of the app (the configured <c>AppUrl</c>), for links sent to customers.</summary>
public interface IAppLinks
{
    /// <summary>Without a trailing slash; null when it is not configured.</summary>
    string? BaseUrl { get; }
}
