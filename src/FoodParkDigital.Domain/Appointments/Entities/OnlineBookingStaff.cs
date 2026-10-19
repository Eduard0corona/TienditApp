using System.Globalization;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Reviews.Entities;
using FoodParkDigital.Domain.Tenants;

namespace FoodParkDigital.Domain.Appointments.Entities;

/// <summary>
/// A team member customers may book online (Fase 24J, ADR 0032): the name customers see, the services they do (all, or
/// some) and their own weekly hours, when they differ from the business's. While at least one is <see cref="Enabled"/>, the
/// business's online times come from each one of them instead of how many appointments it attends at once. Turned off, it
/// keeps what was set, to turn it on again. Only the business's owner or an administrator changes it.
/// </summary>
public class OnlineBookingStaff : AuditableEntity, ITenantScoped, IAggregateRoot
{
    public const int MaxNameLength = 40;

    /// <summary>What the name may take stored: forty characters with a few emoji, which take more than one unit each.</summary>
    public const int MaxStoredNameLength = 80;

    /// <summary>People a business may have taking online appointments at once.</summary>
    public const int MaxEnabled = 30;

    /// <summary>Services one person may list: every service a business books online, at most.</summary>
    public const int MaxServices = 100;

    public const string NameRequiredMessage = "Escribe el nombre que verán tus clientes.";
    public const string NameTooLongMessage = "El nombre que ven tus clientes puede tener hasta 40 caracteres.";
    public const string NameContactMessage = "El nombre que ven tus clientes no puede llevar enlaces, correos ni teléfonos.";
    public const string ServicesRequiredMessage = "Elige al menos un servicio, o todos.";
    public const string TooManyServicesMessage = "Una persona puede tener hasta 100 servicios.";

    private readonly List<OnlineBookingStaffService> _services = [];

    private OnlineBookingStaff() { }

    public Guid TenantId { get; private set; }

    /// <summary>The team member's account; one per business. Customers never see it.</summary>
    public Guid UserId { get; private set; }

    /// <summary>How customers see them: «Ana L.».</summary>
    public string Name { get; private set; } = null!;

    /// <summary>Whether customers may book them now.</summary>
    public bool Enabled { get; private set; }

    /// <summary>Whether they do every service the business books online, also the ones it adds later.</summary>
    public bool AllServices { get; private set; }

    /// <summary>
    /// When they attend, inside the business's hours; null when they attend all of them. The business's special days hold
    /// for everyone.
    /// </summary>
    public OpeningHours? WeeklyHours { get; private set; }

    /// <summary>The services they do when not <see cref="AllServices"/>.</summary>
    public IReadOnlyCollection<OnlineBookingStaffService> Services => _services.AsReadOnly();

    public static OnlineBookingStaff Create(
        Guid tenantId,
        Guid userId,
        string name,
        bool enabled,
        bool allServices,
        IEnumerable<Guid> serviceIds,
        OpeningHours? weeklyHours)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty)
            throw new DomainException("La persona necesita un negocio y una cuenta.");

        var staff = new OnlineBookingStaff { TenantId = tenantId, UserId = userId };
        staff.Change(name, enabled, allServices, serviceIds, weeklyHours);
        return staff;
    }

    /// <summary>Replaces everything customers see of them and when they attend.</summary>
    public void Change(string name, bool enabled, bool allServices, IEnumerable<Guid> serviceIds, OpeningHours? weeklyHours)
    {
        var cleaned = name?.Trim();
        if (string.IsNullOrEmpty(cleaned))
            throw new DomainException(NameRequiredMessage);
        if (new StringInfo(cleaned).LengthInTextElements > MaxNameLength || cleaned.Length > MaxStoredNameLength)
            throw new DomainException(NameTooLongMessage);
        if (BusinessReview.HasContactOrLink(cleaned))
            throw new DomainException(NameContactMessage);

        var services = allServices ? [] : serviceIds.Distinct().ToList();
        if (services.Contains(Guid.Empty))
            throw new ArgumentException("A service is required.", nameof(serviceIds));
        if (!allServices && services.Count == 0)
            throw new DomainException(ServicesRequiredMessage);
        if (services.Count > MaxServices)
            throw new DomainException(TooManyServicesMessage);

        Name = cleaned;
        Enabled = enabled;
        AllServices = allServices;
        WeeklyHours = weeklyHours;
        // Keeps the rows that stay: the same service removed and added again would be two rows with one key.
        _services.RemoveAll(service => !services.Contains(service.ProductId));
        foreach (var serviceId in services.Where(id => _services.All(service => service.ProductId != id)))
            _services.Add(new OnlineBookingStaffService(TenantId, Id, serviceId));
    }

    /// <summary>Whether they do a service.</summary>
    public bool Offers(Guid serviceId) => AllServices || _services.Any(service => service.ProductId == serviceId);

    /// <summary>
    /// The name to suggest from the account: «Ana L.», the first name and the initial of the last one, cut to fit; empty
    /// without a first name (the owner writes one).
    /// </summary>
    public static string SuggestedName(string? firstName, string? lastName)
    {
        var first = firstName?.Trim();
        if (string.IsNullOrEmpty(first))
            return string.Empty;
        var last = lastName?.Trim();
        var initial = string.IsNullOrEmpty(last) ? string.Empty : $" {StringInfo.GetNextTextElement(last).ToUpperInvariant()}.";
        var room = MaxNameLength - initial.Length;
        var whole = new StringInfo(first);
        var cut = whole.LengthInTextElements > room ? whole.SubstringByTextElements(0, room).TrimEnd() : first;
        return cut + initial;
    }
}

/// <summary>A service a person who takes online appointments does (<see cref="OnlineBookingStaff"/>).</summary>
public class OnlineBookingStaffService : ITenantScoped
{
    private OnlineBookingStaffService() { }

    internal OnlineBookingStaffService(Guid tenantId, Guid staffId, Guid productId)
    {
        TenantId = tenantId;
        StaffId = staffId;
        ProductId = productId;
    }

    public Guid TenantId { get; private set; }
    public Guid StaffId { get; private set; }

    /// <summary>The service, in the catalog. Deleting the service deletes this row.</summary>
    public Guid ProductId { get; private set; }
}
