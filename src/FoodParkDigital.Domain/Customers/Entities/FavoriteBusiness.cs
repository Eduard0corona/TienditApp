using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Customers.Entities;

/// <summary>
/// A business a customer account keeps among its favorites (Fase 24F), to find it again first in Explorar. It belongs to
/// the account, not to the business: the business does not see who keeps it, and <see cref="BusinessId"/> is the business
/// it points to, not an owner.
/// </summary>
public class FavoriteBusiness : AuditableEntity, IAggregateRoot
{
    /// <summary>The businesses one account keeps.</summary>
    public const int MaxPerAccount = 100;

    public const string AccountRequiredMessage = "El favorito necesita una cuenta.";
    public const string BusinessRequiredMessage = "El favorito necesita un negocio.";
    public const string TooManyMessage = "Guardas hasta 100 negocios favoritos: quita uno para agregar otro.";

    private FavoriteBusiness() { }

    private FavoriteBusiness(Guid userId, Guid businessId)
    {
        UserId = userId;
        BusinessId = businessId;
    }

    public Guid UserId { get; private set; }

    /// <summary>The business kept: its public menu is where the favorite leads.</summary>
    public Guid BusinessId { get; private set; }

    /// <summary>
    /// A new favorite of the account while it keeps fewer than <see cref="MaxPerAccount"/>. Keeping the same business twice
    /// is the caller's to avoid (one favorite per business).
    /// </summary>
    /// <param name="favoritesKept">How many businesses the account keeps now.</param>
    public static FavoriteBusiness Create(Guid userId, Guid businessId, int favoritesKept)
    {
        if (userId == Guid.Empty)
            throw new DomainException(AccountRequiredMessage);
        if (businessId == Guid.Empty)
            throw new DomainException(BusinessRequiredMessage);
        if (favoritesKept >= MaxPerAccount)
            throw new DomainException(TooManyMessage);

        return new FavoriteBusiness(userId, businessId);
    }
}
