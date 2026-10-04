namespace FoodParkDigital.Domain.Tenants.Enums;

public enum TenantPermission
{
    ManageCatalog = 1,
    ManageOrders = 2,
    ManageServiceBoard = 3,
    ManageInventory = 4,
    ManageCash = 5,
    ManageCustomers = 6,
    ViewReports = 7,
    ManageWhatsApp = 8,
    ManageAiAgent = 9,
    ManageEmployees = 10,
    ManageAppointments = 11,
    /// <summary>Give money back and void paid sales. Owners and admins always can; staff only when granted.</summary>
    ProcessRefunds = 12,
    /// <summary>Message templates, campaigns and reminders. Owners and admins always can; staff only when granted.</summary>
    ManageCampaigns = 13,
    /// <summary>
    /// Charge: register payments and close sales. <see cref="ManageOrders"/> takes orders; without this a waiter leaves
    /// them for the cashier.
    /// </summary>
    ChargeOrders = 14
}
