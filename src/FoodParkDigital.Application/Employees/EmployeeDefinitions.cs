using FoodParkDigital.Application.Employees.DTOs;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Employees;

public static class EmployeeDefinitions
{
    public static string ToIdentityRole(UserRole role) => role switch
    {
        UserRole.SuperAdmin => "SUPER_ADMIN",
        UserRole.TenantAdmin => "TENANT_ADMIN",
        UserRole.Staff => "STAFF",
        UserRole.Kitchen => "KITCHEN",
        UserRole.Customer => "CUSTOMER",
        _ => "STAFF"
    };

    public static IReadOnlyList<EmployeePermissionOptionDto> PermissionOptions { get; } =
    [
        new() { Code = nameof(TenantPermission.ManageCatalog), Name = "Catalogo", Description = "Gestionar productos, categorias y servicios." },
        new() { Code = nameof(TenantPermission.ManageOrders), Name = "Ordenes", Description = "Crear, actualizar y operar ordenes." },
        new() { Code = nameof(TenantPermission.ManageServiceBoard), Name = "Tablero de servicios", Description = "Actualizar piezas y estados operativos." },
        new() { Code = nameof(TenantPermission.ManageInventory), Name = "Inventario", Description = "Administrar insumos, stock, compras y proveedores." },
        new() { Code = nameof(TenantPermission.ManageCash), Name = "Caja", Description = "Registrar pagos, movimientos y cortes." },
        new() { Code = nameof(TenantPermission.ManageCustomers), Name = "Clientes", Description = "Administrar clientes del negocio." },
        new() { Code = nameof(TenantPermission.ViewReports), Name = "Reportes", Description = "Visualizar dashboards, reportes y auditoria." },
        new() { Code = nameof(TenantPermission.ManageWhatsApp), Name = "WhatsApp", Description = "Acceder a configuracion y chat de WhatsApp." },
        new() { Code = nameof(TenantPermission.ManageAiAgent), Name = "Respuestas automáticas", Description = "Configurar las respuestas automáticas de WhatsApp." },
        new() { Code = nameof(TenantPermission.ManageEmployees), Name = "Empleados", Description = "Administrar usuarios empleados, roles y permisos." },
        new() { Code = nameof(TenantPermission.ManageAppointments), Name = "Citas", Description = "Gestionar agenda y citas." },
        new() { Code = nameof(TenantPermission.ProcessRefunds), Name = "Reembolsos", Description = "Devolver dinero y anular ventas pagadas." },
        new() { Code = nameof(TenantPermission.ManageCampaigns), Name = "Campañas y recordatorios", Description = "Plantillas, campañas por WhatsApp y recordatorios de citas." }
    ];
}
