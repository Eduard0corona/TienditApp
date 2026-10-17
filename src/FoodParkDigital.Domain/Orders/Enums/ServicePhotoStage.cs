namespace FoodParkDigital.Domain.Orders.Enums;

/// <summary>When a photo of a service was taken (Fase 25): as it was received, or as it is delivered.</summary>
public enum ServicePhotoStage
{
    /// <summary>Before: how the customer left it (a stain, a scratch).</summary>
    Before = 0,
    /// <summary>After: how it is delivered, the work done.</summary>
    After = 1
}
