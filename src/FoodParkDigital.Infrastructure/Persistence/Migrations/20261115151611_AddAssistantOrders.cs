using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AssistantOrders",
                table: "TenantSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "AssistantOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Token = table.Column<string>(type: "varchar(43)", unicode: false, maxLength: 43, nullable: false),
                    PostalCode = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: true),
                    ItemsTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PreparedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Lines = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantOrders", x => x.Id);
                    table.CheckConstraint("CK_AssistantOrders_ItemsTotal", "[ItemsTotal] >= 0");
                    table.CheckConstraint("CK_AssistantOrders_Outcome", "([Status] = 0 AND [OrderId] IS NULL AND [ConfirmedAt] IS NULL) OR ([Status] = 1 AND [OrderId] IS NOT NULL AND [ConfirmedAt] IS NOT NULL)");
                    table.CheckConstraint("CK_AssistantOrders_PostalCode", "[PostalCode] IS NULL OR LEN([PostalCode]) = 5");
                    table.CheckConstraint("CK_AssistantOrders_Status", "[Status] IN (0, 1)");
                    table.CheckConstraint("CK_AssistantOrders_Token", "LEN([Token]) = 43");
                    table.ForeignKey(
                        name: "FK_AssistantOrders_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AssistantOrders_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantOrders_ExpiresAt",
                table: "AssistantOrders",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantOrders_OrderId",
                table: "AssistantOrders",
                column: "OrderId",
                unique: true,
                filter: "[OrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantOrders_TenantId_Status_ExpiresAt",
                table: "AssistantOrders",
                columns: new[] { "TenantId", "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantOrders_Token",
                table: "AssistantOrders",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fase 32B (ADR 0096): rolling back would lose which orders customers sent from an assistant's link, the links still
            // open (they would stop filling the cart), or a business that turned these orders off (it would get them again when
            // rolled forward). Once there is one, it stops before anything changes; query 2 of verify.sql counts them. Expired
            // links that were never sent may go. A missing table compiles (deferred name resolution): a second run skips it.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [AssistantOrders] WITH (TABLOCKX, HOLDLOCK) WHERE [Status] = 1 OR [ExpiresAt] > SYSDATETIMEOFFSET())
    OR EXISTS (SELECT 1 FROM [TenantSettings] WITH (TABLOCKX, HOLDLOCK) WHERE [AssistantOrders] = 0)
    THROW 50064, N''Customers sent orders from an assistant, links are still open or a business turned these orders off; rolling back would lose it (query 2 of verify.sql). Roll forward instead.'', 1;');
");

            migrationBuilder.DropTable(
                name: "AssistantOrders");

            migrationBuilder.DropColumn(
                name: "AssistantOrders",
                table: "TenantSettings");
        }
    }
}
