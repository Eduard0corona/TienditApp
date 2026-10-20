using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerWriteOffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CreditTermDays",
                table: "TenantSettings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WrittenOffAmount",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "OrderWriteOffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    WrittenOffAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    WrittenOffBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderWriteOffs", x => x.Id);
                    table.CheckConstraint("CK_OrderWriteOffs_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_OrderWriteOffs_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TenantSettings_CreditTermDays",
                table: "TenantSettings",
                sql: "[CreditTermDays] IS NULL OR ([CreditTermDays] >= 1 AND [CreditTermDays] <= 120)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_WrittenOffAmount",
                table: "Orders",
                sql: "[WrittenOffAmount] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_OrderWriteOffs_OrderId",
                table: "OrderWriteOffs",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderWriteOffs_TenantId_OrderId",
                table: "OrderWriteOffs",
                columns: new[] { "TenantId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderWriteOffs_TenantId_WrittenOffAt",
                table: "OrderWriteOffs",
                columns: new[] { "TenantId", "WrittenOffAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A write-off is the business's word that a debt is forgiven, and the term is when what customers owe is overdue:
            // rolling back would bring the forgiven debts back as owed and forget the term, so once a business uses either
            // there is only roll-forward. The check runs as dynamic SQL: a second run of rollback.sql, with the column gone,
            // skips it without compiling it (a missing column, unlike a missing table, would not compile).
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [OrderWriteOffs]) OR EXISTS (SELECT 1 FROM [TenantSettings] WHERE [CreditTermDays] IS NOT NULL)
    THROW 50027, N''Businesses wrote off customer debts or set a credit term already; rolling back would bring forgiven debts back as owed and forget the term. Roll forward instead.'', 1;');
");

            migrationBuilder.DropTable(
                name: "OrderWriteOffs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TenantSettings_CreditTermDays",
                table: "TenantSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_WrittenOffAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CreditTermDays",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "WrittenOffAmount",
                table: "Orders");
        }
    }
}
