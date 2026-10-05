using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Customers.Entities;

public class Customer : AuditableEntity, ITenantScoped
{
    public const int NotesMaxLength = 2000;
    public const int MaxAddresses = 10;

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

