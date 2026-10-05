namespace FoodParkDigital.Application.Common.Exceptions;

/// <summary>The user is signed in but may not do this (HTTP 403).</summary>
public sealed class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException(string message)
        : base(message)
    {
    }

    private ForbiddenAccessException(string message, bool moduleDisabled)
        : base(message)
    {
        ModuleDisabled = moduleDisabled;
    }

    /// <summary>Refused because the business has not enabled the module, not because of the person's role.</summary>
    public bool ModuleDisabled { get; }

    public static ForbiddenAccessException ForDisabledModule(string message) => new(message, moduleDisabled: true);
}
