using System.Net;
using FoodParkDigital.Infrastructure.Tests.Support;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Signing up on the real API (SQL Server, the whole pipeline): an email that already has an account gets the same
/// answer as a new one, and only its owner learns, by email, that it already had one. The emails go out from the
/// outbox, after the answer.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class AccountEmailsSqlServerTests
{
    private readonly TwoBusinessesApi _api;

    public AccountEmailsSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task SigningUpWithAnEmailThatHasAnAccount_ShouldAnswerLikeANewOne_AndOnlyEmailItsOwner()
    {
        var owner = await _api.SignUpAsync("correo-tomado");
        var newEmail = $"nuevo-{Guid.NewGuid():N}@demo.com";

        var taken = await _api.RegisterBusinessRequestAsync(owner.Email);
        var free = await _api.RegisterBusinessRequestAsync(newEmail);

        Assert.Equal(HttpStatusCode.OK, taken.Status);
        Assert.Equal(free, taken);

        await _api.DispatchOutboxAsync();
        Assert.Equal(1, _api.AlreadyExistsEmailsTo(owner.Email));
        // The owner's confirmation went out at sign-up; signing up again sent no other.
        Assert.Equal(1, _api.ConfirmationsTo(owner.Email));
        Assert.Equal(1, _api.ConfirmationsTo(newEmail));
        Assert.Equal(0, _api.AlreadyExistsEmailsTo(newEmail));
    }
}
