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
        new() { Code = nameof(TenantPermission.ManageOrders), Name = "Tomar pedidos", Description = "Vender, levantar pedidos y cuentas de mesa, y cambiar su estado." },
        new() { Code = nameof(TenantPermission.ChargeOrders), Name = "Cobrar", Description = "Registrar pagos y cerrar ventas. Sin este permiso, los pedidos quedan para que los cobre alguien más." },
        new() { Code = nameof(TenantPermission.ManageCash), Name = "Caja", Description = "Abrir y cerrar su caja, gastos, retiros y cortes." },
        new() { Code = nameof(TenantPermission.ManageServiceBoard), Name = "Cocina y taller", Description = "Ver las comandas y los trabajos del taller y marcarlos listos." },
        new() { Code = nameof(TenantPermission.ManageCatalog), Name = "Catálogo", Description = "Productos, categorías y servicios." },
        new() { Code = nameof(TenantPermission.ManageInventory), Name = "Inventario", Description = "Insumos, existencias, compras y proveedores." },
        new() { Code = nameof(TenantPermission.ManageCustomers), Name = "Clientes", Description = "Clientes del negocio, sus notas y direcciones." },
        new() { Code = nameof(TenantPermission.ManageAppointments), Name = "Agenda", Description = "Citas y reservaciones." },
        new() { Code = nameof(TenantPermission.ManageWhatsApp), Name = "Mensajes", Description = "Chat de WhatsApp con los clientes." },
        new() { Code = nameof(TenantPermission.ManageAiAgent), Name = "Respuestas automáticas", Description = "Configurar las respuestas automáticas de WhatsApp." },
        new() { Code = nameof(TenantPermission.ManageCampaigns), Name = "Campañas y recordatorios", Description = "Plantillas, campañas por WhatsApp y recordatorios de citas." },
        new() { Code = nameof(TenantPermission.ViewReports), Name = "Reportes", Description = "Inicio, reportes y ventas por sucursal." },
        new() { Code = nameof(TenantPermission.ProcessRefunds), Name = "Reembolsos", Description = "Devolver dinero y anular ventas pagadas." }
        // The team (ManageEmployees) is managed by owners and admins only: whoever manages permissions could give
        // themselves all of them, so it is not offered to staff.
    ];

    /// <summary>
    /// Roles named like the business names them. Choosing one fills the permissions; changing any permission after
    /// that makes it a custom role. The permissions an employee ends up with are always the ones saved.
    /// </summary>
    public static IReadOnlyList<EmployeeRolePresetDto> RolePresets { get; } =
    [
        new()
        {
            Key = "manager", Name = "Gerente", Description = "Administra el negocio: equipo, catálogo, reportes y configuración.",
            AppRole = UserRole.TenantAdmin, TenantRole = TenantUserRole.Admin,
            Permissions = Enum.GetNames<TenantPermission>().ToList()
        },
        new()
        {
            Key = "cashier", Name = "Cajero", Description = "Vende, cobra, abre y cierra su caja y atiende el chat.",
            AppRole = UserRole.Staff, TenantRole = TenantUserRole.Staff,
            Permissions = Codes(TenantPermission.ManageOrders, TenantPermission.ChargeOrders, TenantPermission.ManageCash,
                TenantPermission.ManageCustomers, TenantPermission.ManageWhatsApp, TenantPermission.ManageAppointments)
        },
        new()
        {
            Key = "waiter", Name = "Mesero", Description = "Toma pedidos y cuentas de mesa; no cobra ni ve la caja.",
            AppRole = UserRole.Staff, TenantRole = TenantUserRole.Staff,
            Permissions = Codes(TenantPermission.ManageOrders)
        },
        new()
        {
            Key = "kitchen", Name = "Cocina", Description = "Ve las comandas y las marca listas.",
            AppRole = UserRole.Kitchen, TenantRole = TenantUserRole.Staff,
            Permissions = Codes(TenantPermission.ManageOrders, TenantPermission.ManageServiceBoard)
        },
        new()
        {
            Key = "technician", Name = "Técnico", Description = "Toma los trabajos del taller y los marca terminados.",
            AppRole = UserRole.Staff, TenantRole = TenantUserRole.Staff,
            Permissions = Codes(TenantPermission.ManageServiceBoard, TenantPermission.ManageAppointments)
        }
    ];

    private static List<string> Codes(params TenantPermission[] permissions) => permissions.Select(p => p.ToString()).ToList();
}
