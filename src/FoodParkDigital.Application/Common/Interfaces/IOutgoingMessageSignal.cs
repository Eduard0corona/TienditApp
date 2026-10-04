namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>Wakes the messaging worker after a campaign is queued, so it does not wait for its next poll.</summary>
public interface IOutgoingMessageSignal
{
    void Notify();
}
