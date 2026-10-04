using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Employees.DTOs;

public class EmployeeDto
{
    public Guid TenantUserId { get; set; }
    public Guid UserId { get; set; }
    public string Email { get; set; } = null!;
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string? PhoneNumber { get; set; }
    public string AppRole { get; set; } = null!;
    public string TenantRole { get; set; } = null!;
    public string Status { get; set; } = null!;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<string> Permissions { get; set; } = [];
    /// <summary>Only right after inviting someone new: the link to set their password, to share it by WhatsApp.</summary>
    public string? InvitationUrl { get; set; }
}

/// <summary>The link where an invited person chooses their password, relative to the app.</summary>
public class EmployeeInvitationDto
{
    public string InvitationUrl { get; set; } = null!;
    public string Email { get; set; } = null!;
}

public class EmployeePermissionOptionDto
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
}

/// <summary>A named role (Gerente, Cajero, Mesero...) that fills the account role and the permissions.</summary>
public class EmployeeRolePresetDto
{
    public string Key { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public UserRole AppRole { get; set; }
    public TenantUserRole TenantRole { get; set; }
    public List<string> Permissions { get; set; } = [];
}
