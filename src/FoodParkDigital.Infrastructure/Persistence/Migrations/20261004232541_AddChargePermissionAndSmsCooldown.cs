using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChargePermissionAndSmsCooldown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SmsCodeSentAt",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true);

            // Until now taking orders (ManageOrders) also charged them; charging is its own permission now
            // (ChargeOrders). Whoever has saved permissions that take orders keeps charging, except kitchen accounts
            // (3 = UserRole.Kitchen), which never could. Memberships without saved permissions take the defaults of
            // their role, which include charging for staff; "None" is "sin acceso" and stays as it is.
            migrationBuilder.Sql(@"
UPDATE tu SET [PermissionCodes] = tu.[PermissionCodes] + N',ChargeOrders'
FROM [TenantUsers] tu
INNER JOIN [AspNetUsers] u ON u.[Id] = tu.[UserId]
WHERE tu.[PermissionCodes] IS NOT NULL
  AND (N',' + tu.[PermissionCodes] + N',') LIKE N'%,ManageOrders,%'
  AND (N',' + tu.[PermissionCodes] + N',') NOT LIKE N'%,ChargeOrders,%'
  AND u.[Role] <> 3;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The ChargeOrders codes stay: the previous code ignores permission codes it does not know.
            migrationBuilder.DropColumn(
                name: "SmsCodeSentAt",
                table: "AspNetUsers");
        }
    }
}
