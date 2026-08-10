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
}

public class EmployeePermissionOptionDto
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
}
