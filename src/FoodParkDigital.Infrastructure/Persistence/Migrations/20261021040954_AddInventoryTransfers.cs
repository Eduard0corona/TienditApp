using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TransferredBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OutMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineCount = table.Column<int>(type: "int", nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryTransfers", x => x.Id);
                    table.CheckConstraint("CK_InventoryTransfers_Lines", "[LineCount] BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_InventoryTransfers_Locations", "[FromLocationId] <> [ToLocationId]");
                    table.CheckConstraint("CK_InventoryTransfers_Movements", "[OutMovementId] <> [InMovementId]");
                    table.CheckConstraint("CK_InventoryTransfers_TotalCost", "[TotalCost] >= 0");
                    table.ForeignKey(
                        name: "FK_InventoryTransfers_InventoryLocations_FromLocationId",
                        column: x => x.FromLocationId,
                        principalTable: "InventoryLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransfers_InventoryLocations_ToLocationId",
                        column: x => x.ToLocationId,
                        principalTable: "InventoryLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransfers_InventoryMovements_InMovementId",
                        column: x => x.InMovementId,
                        principalTable: "InventoryMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransfers_InventoryMovements_OutMovementId",
                        column: x => x.OutMovementId,
                        principalTable: "InventoryMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransfers_FromLocationId",
                table: "InventoryTransfers",
                column: "FromLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransfers_InMovementId",
                table: "InventoryTransfers",
                column: "InMovementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransfers_OutMovementId",
                table: "InventoryTransfers",
                column: "OutMovementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransfers_TenantId_IdempotencyKey",
                table: "InventoryTransfers",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransfers_TenantId_TransferNumber",
                table: "InventoryTransfers",
                columns: new[] { "TenantId", "TransferNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransfers_TenantId_TransferredAt",
                table: "InventoryTransfers",
                columns: new[] { "TenantId", "TransferredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransfers_ToLocationId",
                table: "InventoryTransfers",
                column: "ToLocationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A transfer ties its two movements together: rolling back would leave each leg alone, an exit and an entry
            // nobody could tell apart from manual ones. Once a business makes one there is only roll-forward. The check runs
            // as dynamic SQL, as the previous rollbacks do.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [InventoryTransfers])
    THROW 50031, N''Businesses made transfers between locations already; rolling back would erase which exit and which entry belong together, who moved the items and why. Roll forward instead.'', 1;');
");

            migrationBuilder.DropTable(
                name: "InventoryTransfers");
        }
    }
}
