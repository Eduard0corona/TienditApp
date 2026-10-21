namespace FoodParkDigital.Infrastructure.Services;

/// <summary>
/// The application locks of SQL Server that serialize requests (<see cref="InventoryLocationLock"/>,
/// <see cref="CustomerCreditLock"/>, <see cref="AppointmentBookingLock"/>): each one throws this error when the lock is not
/// granted within its wait, which the API answers as a conflict to try again.
/// </summary>
public static class ApplicationLocks
{
    public const int NotGrantedErrorNumber = 51000;
}
