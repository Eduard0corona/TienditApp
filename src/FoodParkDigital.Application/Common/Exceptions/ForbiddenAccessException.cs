namespace FoodParkDigital.Application.Common.Exceptions;

/// <summary>The user is signed in but may not do this (HTTP 403).</summary>
public sealed class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException(string message)
        : base(message)
    {
    }
}
