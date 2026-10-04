using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchesAndDailySalesSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "InventoryLocations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "CashSessions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AddressLine1 = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    AddressLine2 = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    City = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    State = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ContactName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ContactPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailySalesSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OrderType = table.Column<int>(type: "int", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OrdersCreated = table.Column<int>(type: "int", nullable: false),
                    PaidOrders = table.Column<int>(type: "int", nullable: false),
                    PaymentsCount = table.Column<int>(type: "int", nullable: false),
                    PaymentsAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CashAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CardAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TransferAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    WalletAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OtherAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RefundsCount = table.Column<int>(type: "int", nullable: false),
                    RefundsAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailySalesSnapshots", x => x.Id);
                });

            // Before branches existed, each stock location worked as a branch: every location becomes a branch with the
            // same Id and data (payments, refunds and drawers that pointed at it keep pointing at the same place), and
            // the location belongs to it. One branch per business is the default: its default active location, else
            // the first active one, else the first one. Businesses without locations get a default branch.
            migrationBuilder.Sql(@"
INSERT INTO [Branches] ([Id], [TenantId], [Name], [Code], [Description], [AddressLine1], [AddressLine2], [City], [State],
    [Country], [PostalCode], [ContactName], [ContactPhone], [IsDefault], [IsActive], [CreatedAt], [CreatedBy], [UpdatedAt], [UpdatedBy])
SELECT l.[Id], l.[TenantId], l.[Name], l.[Code], l.[Description], l.[AddressLine1], l.[AddressLine2], l.[City], l.[State],
    l.[Country], l.[PostalCode], l.[ContactName], l.[ContactPhone], 0, l.[IsActive], l.[CreatedAt], l.[CreatedBy], l.[UpdatedAt], l.[UpdatedBy]
FROM [InventoryLocations] l;

WITH ranked AS (
    SELECT l.[Id], ROW_NUMBER() OVER (
        PARTITION BY l.[TenantId]
        ORDER BY CASE WHEN l.[IsDefault] = 1 AND l.[IsActive] = 1 THEN 0 WHEN l.[IsActive] = 1 THEN 1 ELSE 2 END, l.[CreatedAt], l.[Id]) AS rn
    FROM [InventoryLocations] l
)
UPDATE b SET [IsDefault] = 1, [IsActive] = 1
FROM [Branches] b
INNER JOIN ranked r ON r.[Id] = b.[Id]
WHERE r.rn = 1;

INSERT INTO [Branches] ([Id], [TenantId], [Name], [Code], [IsDefault], [IsActive], [CreatedAt], [CreatedBy])
SELECT NEWID(), t.[Id], N'Sucursal principal', N'PRINCIPAL', 1, 1, SYSDATETIMEOFFSET(), N'migration'
FROM [Tenants] t
WHERE NOT EXISTS (SELECT 1 FROM [Branches] b WHERE b.[TenantId] = t.[Id]);

UPDATE [InventoryLocations] SET [BranchId] = [Id];
");

            // Drawers stay at the branch of their location; drawers without one go to the default branch.
            migrationBuilder.Sql(@"
UPDATE [CashSessions] SET [BranchId] = [InventoryLocationId] WHERE [InventoryLocationId] IS NOT NULL;

UPDATE s SET [BranchId] = b.[Id]
FROM [CashSessions] s
INNER JOIN [Branches] b ON b.[TenantId] = s.[TenantId] AND b.[IsDefault] = 1
WHERE s.[BranchId] IS NULL;
");

            // An order was made where its first payment was taken (the payment's location is now its branch); orders
            // without payments, or paid without location, go to the default branch.
            migrationBuilder.Sql(@"
UPDATE o SET [BranchId] = p.[InventoryLocationId]
FROM [Orders] o
CROSS APPLY (
    SELECT TOP (1) op.[InventoryLocationId]
    FROM [OrderPayments] op
    WHERE op.[TenantId] = o.[TenantId] AND op.[OrderId] = o.[Id] AND op.[InventoryLocationId] IS NOT NULL
    ORDER BY op.[PaidAt], op.[Id]) p;

UPDATE o SET [BranchId] = b.[Id]
FROM [Orders] o
INNER JOIN [Branches] b ON b.[TenantId] = o.[TenantId] AND b.[IsDefault] = 1
WHERE o.[BranchId] IS NULL;
");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_BranchId",
                table: "Orders",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_BranchId_CreatedAt",
                table: "Orders",
                columns: new[] { "TenantId", "BranchId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLocations_BranchId",
                table: "InventoryLocations",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLocations_TenantId_BranchId",
                table: "InventoryLocations",
                columns: new[] { "TenantId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_BranchId",
                table: "CashSessions",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_TenantId_BranchId_OpenedAt",
                table: "CashSessions",
                columns: new[] { "TenantId", "BranchId", "OpenedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Branches_TenantId_Code",
                table: "Branches",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Branches_TenantId_Default",
                table: "Branches",
                column: "TenantId",
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_TenantId_IsActive",
                table: "Branches",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_DailySalesSnapshots_TenantId_BusinessDate_BranchId_OrderType",
                table: "DailySalesSnapshots",
                columns: new[] { "TenantId", "BusinessDate", "BranchId", "OrderType" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CashSessions_Branches_BranchId",
                table: "CashSessions",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryLocations_Branches_BranchId",
                table: "InventoryLocations",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Branches_BranchId",
                table: "Orders",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back drops the branches. Branches created after this migration (not one of the former locations)
            // and their data would be lost silently: refuse instead, so they are handled by hand first.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [Branches] b WHERE NOT EXISTS (SELECT 1 FROM [InventoryLocations] l WHERE l.[Id] = b.[Id]))
    THROW 50003, N'There are branches that were not inventory locations; rolling back would lose them.', 1;
");

            migrationBuilder.DropForeignKey(
                name: "FK_CashSessions_Branches_BranchId",
                table: "CashSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryLocations_Branches_BranchId",
                table: "InventoryLocations");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Branches_BranchId",
                table: "Orders");

            migrationBuilder.DropTable(
                name: "Branches");

            migrationBuilder.DropTable(
                name: "DailySalesSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_Orders_BranchId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_TenantId_BranchId_CreatedAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_InventoryLocations_BranchId",
                table: "InventoryLocations");

            migrationBuilder.DropIndex(
                name: "IX_InventoryLocations_TenantId_BranchId",
                table: "InventoryLocations");

            migrationBuilder.DropIndex(
                name: "IX_CashSessions_BranchId",
                table: "CashSessions");

            migrationBuilder.DropIndex(
                name: "IX_CashSessions_TenantId_BranchId_OpenedAt",
                table: "CashSessions");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "InventoryLocations");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "CashSessions");
        }
    }
}
