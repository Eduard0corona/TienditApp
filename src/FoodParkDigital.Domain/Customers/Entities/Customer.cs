using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Entities;

namespace FoodParkDigital.Domain.Customers.Entities;

public class Customer : AuditableEntity, ITenantScoped
{
    public const int NotesMaxLength = 2000;
    public const int MaxAddresses = 10;
    /// <summary>The most a business lets one customer owe (Fase 26A).</summary>
    public const decimal MaxCreditLimit = 1_000_000m;
    public const string CreditLimitRangeMessage = "El límite de fiado va de $1 a $1,000,000.";
    public const string PublicGeneralCreditMessage = "A «Público en general» no se le fía: elige al cliente.";
    public const string NoPhoneCreditMessage = "Para fiarle, el cliente necesita teléfono: con él se juntan sus pedidos.";
    public const string CreditTermRangeMessage = "El plazo de un cliente va de 1 a 120 días, o el del negocio.";
    public const string PublicGeneralCreditTermMessage = "«Público en general» no tiene plazo propio: elige al cliente.";
    public const string NoPhoneCreditTermMessage = "Para darle un plazo, el cliente necesita teléfono: con él se juntan sus pedidos.";

    private readonly List<CustomerAddress> _addresses = new();

    private Customer() { }

    public Customer(
        Guid tenantId,
        string name,
        string? phone,
        string? email,
        bool isPublicGeneral = false)
    {
        TenantId = tenantId;
        Name = name;
        Phone = phone;
        Email = email;
        IsPublicGeneral = isPublicGeneral;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public bool IsPublicGeneral { get; private set; }
    public bool IsActive { get; private set; }
    /// <summary>What the team should remember about the customer: preferences, allergies, how they pay.</summary>
    public string? Notes { get; private set; }
    public IReadOnlyCollection<CustomerAddress> Addresses => _addresses.AsReadOnly();

    /// <summary>
    /// How much the business lets the customer owe (fiado, Fase 26A, ADR 0033); null when it does not sell to them on credit.
    /// What they owe is not kept here: it is the balance of their orders, matched by phone.
    /// </summary>
    public decimal? CreditLimit { get; private set; }

    /// <summary>
    /// How many days the customer has to pay what they owe (Fase 26C, ADR 0035), instead of the business's term; null when
    /// the business's term applies. Like that one, it only tells what is overdue.
    /// </summary>
    public int? CreditTermDays { get; private set; }

    /// <summary>The business sells to them on credit now: active, with a limit and a phone to gather their orders by.</summary>
    public bool SellsOnCredit => IsActive && !IsPublicGeneral && CreditLimit is not null && !string.IsNullOrWhiteSpace(Phone);

    /// <summary>Sets how much the customer may owe, or stops selling to them on credit (null). Lowering it below what they owe
    /// is allowed: it only stops the next sale on credit.</summary>
    public void SetCreditLimit(decimal? limit)
    {
        if (limit is null)
        {
            CreditLimit = null;
            return;
        }

        if (IsPublicGeneral)
            throw new DomainException(PublicGeneralCreditMessage);
        if (string.IsNullOrWhiteSpace(Phone))
            throw new DomainException(NoPhoneCreditMessage);
        if (limit < 1m || limit > MaxCreditLimit || decimal.Round(limit.Value, 2) != limit.Value)
            throw new DomainException(CreditLimitRangeMessage);
        CreditLimit = limit;
    }

    /// <summary>Gives the customer a term of their own, or the business's again (null).</summary>
    public void SetCreditTermDays(int? days)
    {
        if (days is null)
        {
            CreditTermDays = null;
            return;
        }

        if (IsPublicGeneral)
            throw new DomainException(PublicGeneralCreditTermMessage);
        if (string.IsNullOrWhiteSpace(Phone))
            throw new DomainException(NoPhoneCreditTermMessage);
        if (days is < TenantSettings.MinCreditTermDays or > TenantSettings.MaxCreditTermDays)
            throw new DomainException(CreditTermRangeMessage);
        CreditTermDays = days;
    }

    public void Update(string name, string? phone, string? email)
    {
        Name = name;
        Phone = phone;
        Email = email;
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }

    public void UpdateNotes(string? notes)
    {
        var clean = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (clean is { Length: > NotesMaxLength })
            throw new DomainException($"Las notas admiten hasta {NotesMaxLength} caracteres.");
        Notes = clean;
    }

    /// <summary>The first address becomes the default one; asking for default moves it from the previous one.</summary>
    public CustomerAddress AddAddress(string label, string addressLine, string? references, bool makeDefault = false)
    {
        if (_addresses.Count >= MaxAddresses)
            throw new DomainException($"Un cliente puede guardar hasta {MaxAddresses} direcciones.");

        var address = new CustomerAddress(TenantId, Id, label, addressLine, references, isDefault: _addresses.Count == 0);
        _addresses.Add(address);
        if (makeDefault)
            SetDefaultAddress(address.Id);
        return address;
    }

    public void UpdateAddress(Guid addressId, string label, string addressLine, string? references, bool makeDefault)
    {
        var address = FindAddress(addressId);
        address.Change(label, addressLine, references);
        if (makeDefault)
            SetDefaultAddress(addressId);
    }

    /// <summary>Removing the default address hands that role to the oldest remaining one.</summary>
    public void RemoveAddress(Guid addressId)
    {
        var address = FindAddress(addressId);
        _addresses.Remove(address);
        if (address.IsDefault && _addresses.Count > 0)
            _addresses.OrderBy(a => a.CreatedAt).First().MarkDefault(true);
    }

    public void SetDefaultAddress(Guid addressId)
    {
        var target = FindAddress(addressId);
        foreach (var address in _addresses)
            address.MarkDefault(address == target);
    }

    private CustomerAddress FindAddress(Guid addressId)
        => _addresses.FirstOrDefault(a => a.Id == addressId)
           ?? throw new DomainException("La dirección no es de este cliente.");
}

