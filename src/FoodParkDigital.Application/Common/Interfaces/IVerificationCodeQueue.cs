namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// Sends a verification code after the request that asked for it. An anonymous request then answers in the same time
/// whether the account exists or not, and a failure of the provider does not show only for real accounts.
/// </summary>
public interface IVerificationCodeQueue
{
    /// <summary>
    /// Queues the code. When the queue is full the code is dropped (and the drop logged): the person asks for another
    /// one after the cooldown, and the answer stays the same.
    /// </summary>
    void Enqueue(string phoneNumber, string code);
}
