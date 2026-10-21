using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryCounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CountNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    InventoryLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CategoryName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PostedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IncreaseMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecreaseMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LineCount = table.Column<int>(type: "int", nullable: false),
                    CountedLineCount = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryCounts", x => x.Id);
                    table.CheckConstraint("CK_InventoryCounts_Lines", "[LineCount] >= 0 AND [CountedLineCount] >= 0 AND [CountedLineCount] <= [LineCount]");
                    table.CheckConstraint("CK_InventoryCounts_Status", "([Status] = 1 AND [PostedAt] IS NULL AND [CancelledAt] IS NULL) OR ([Status] = 2 AND [PostedAt] IS NOT NULL AND [PostedBy] IS NOT NULL AND [CancelledAt] IS NULL) OR ([Status] = 3 AND [CancelledAt] IS NOT NULL AND [CancelledBy] IS NOT NULL AND [PostedAt] IS NULL)");
                    table.ForeignKey(
                        name: "FK_InventoryCounts_InventoryLocations_InventoryLocationId",
                        column: x => x.InventoryLocationId,
                        principalTable: "InventoryLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCounts_InventoryMovements_DecreaseMovementId",
                        column: x => x.DecreaseMovementId,
                        principalTable: "InventoryMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCounts_InventoryMovements_IncreaseMovementId",
                        column: x => x.IncreaseMovementId,
                        principalTable: "InventoryMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryCountLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplyItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    SystemQuantity = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    CountedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CountedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryCountLines", x => x.Id);
                    table.CheckConstraint("CK_InventoryCountLines_Counted", "([CountedQuantity] IS NULL AND [SystemQuantity] IS NULL AND [CountedAt] IS NULL AND [CountedBy] IS NULL) OR ([CountedQuantity] >= 0 AND [SystemQuantity] IS NOT NULL AND [CountedAt] IS NOT NULL AND [CountedBy] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_InventoryCountLines_InventoryCounts_CountId",
                        column: x => x.CountId,
                        principalTable: "InventoryCounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCountLines_SupplyItems_SupplyItemId",
                        column: x => x.SupplyItemId,
                        principalTable: "SupplyItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountLines_CountId_SupplyItemId",
                table: "InventoryCountLines",
                columns: new[] { "CountId", "SupplyItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountLines_SupplyItemId",
                table: "InventoryCountLines",
                column: "SupplyItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCounts_DecreaseMovementId",
                table: "InventoryCounts",
                column: "DecreaseMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCounts_IncreaseMovementId",
                table: "InventoryCounts",
                column: "IncreaseMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCounts_InventoryLocationId",
                table: "InventoryCounts",
                column: "InventoryLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCounts_TenantId_CountNumber",
                table: "InventoryCounts",
                columns: new[] { "TenantId", "CountNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCounts_TenantId_InventoryLocationId_Open",
                table: "InventoryCounts",
                columns: new[] { "TenantId", "InventoryLocationId" },
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCounts_TenantId_StartedAt",
                table: "InventoryCounts",
                columns: new[] { "TenantId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A count is the record of what was on the shelf, who counted it and against what: rolling back would erase it,
            // and the adjustments of a posted count would be left without the count that explains them. Once a business
            // starts one there is only roll-forward. The check runs as dynamic SQL, as the previous rollbacks do.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [InventoryCounts])
    THROW 50030, N''Businesses started physical counts already; rolling back would erase what was counted, by whom and against what, and leave their adjustments without the count that explains them. Roll forward instead.'', 1;');
");

            migrationBuilder.DropTable(
                name: "InventoryCountLines");

            migrationBuilder.DropTable(
                name: "InventoryCounts");
        }
    }
}
